using CommunityToolkit.Mvvm.ComponentModel;
using MailTrayNotifier.Constants;
using MailTrayNotifier.Models;
using MailTrayNotifier.Resources;

namespace MailTrayNotifier.ViewModels
{
    /// <summary>
    /// 개별 메일 계정 설정 ViewModel
    /// </summary>
    public partial class MailAccountViewModel : ObservableObject
    {
        private string _pop3Server = string.Empty;
        private int _pop3Port = MailConstants.DefaultPop3Port;
        private string _smtpServer = string.Empty;
        private int _smtpPort = MailConstants.DefaultSmtpPort;
        private bool _useSsl = true;
        private string _userId = string.Empty;
        private string _password = string.Empty;
        private int _refreshMinutes = MailConstants.DefaultRefreshMinutes;
        private string _mailWebUrl = string.Empty;
        private bool _isExpanded = false;
        private bool _isEnabled = true;
        private string _accountName = string.Empty;
        private bool _isEditMode;
        private bool _hasError;
        private string _errorMessage = string.Empty;
        private bool _suppressExpandedEvent; // 이벤트 발생 억제 플래그

        // 백업 객체 (Memento 패턴)
        private AccountBackup? _backup;

        /// <summary>
        /// 새로 추가된 계정인지 여부 (저장된 적 없는 계정)
        /// </summary>
        private bool _isNewAccount;

        /// <summary>
        /// 기본 생성자 (새 계정 생성용)
        /// </summary>
        public MailAccountViewModel()
        {
            // 새 계정은 자동으로 편집 모드
            IsEditMode = true;
            _isNewAccount = true;
        }

        /// <summary>
        /// MailSettings에서 생성하는 생성자
        /// </summary>
        public MailAccountViewModel(MailSettings settings)
        {
            _pop3Server = settings.Pop3Server;
            _pop3Port = settings.Pop3Port;
            _smtpServer = settings.SmtpServer;
            _smtpPort = settings.SmtpPort;
            _useSsl = settings.UseSsl;
            _userId = settings.UserId;
            _password = settings.Password;
            _refreshMinutes = settings.RefreshMinutes;
            _mailWebUrl = settings.MailWebUrl;
            _isEnabled = settings.IsEnabled;
            _accountName = settings.AccountName;
        }

        /// <summary>
        /// POP3 서버 주소
        /// </summary>
        public string Pop3Server
        {
            get => _pop3Server;
            set
            {
                if (SetProperty(ref _pop3Server, value))
                {
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        /// <summary>
        /// POP3 포트
        /// </summary>
        public int Pop3Port
        {
            get => _pop3Port;
            set => SetProperty(ref _pop3Port, value);
        }

        /// <summary>
        /// POP3 포트 텍스트 (TextBox 바인딩용)
        /// </summary>
        public string Pop3PortText
        {
            get => _pop3Port.ToString();
            set
            {
                // LostFocus 시에만 호출되므로 최종 검증 수행
                if (string.IsNullOrWhiteSpace(value))
                {
                    // 빈 값이면 기본값 995로 설정 (SSL POP3)
                    Pop3Port = 995;
                }
                else if (int.TryParse(value, out var port))
                {
                    if (port > 0 && port <= 65535)
                    {
                        Pop3Port = port;
                    }
                    else
                    {
                        // 포트 범위를 벗어나면 기본값 995로 설정
                        Pop3Port = 995;
                    }
                }
                else
                {
                    // 숫자가 아닌 값이면 기본값 995로 설정
                    Pop3Port = 995;
                }
                
                // UI 업데이트를 위해 PropertyChanged 이벤트 발생
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// SMTP 서버 주소
        /// </summary>
        public string SmtpServer
        {
            get => _smtpServer;
            set => SetProperty(ref _smtpServer, value);
        }

        /// <summary>
        /// SMTP 포트
        /// </summary>
        public int SmtpPort
        {
            get => _smtpPort;
            set => SetProperty(ref _smtpPort, value);
        }

        /// <summary>
        /// SMTP 포트 텍스트 (TextBox 바인딩용)
        /// </summary>
        public string SmtpPortText
        {
            get => _smtpPort.ToString();
            set
            {
                // LostFocus 시에만 호출되므로 최종 검증 수행
                if (string.IsNullOrWhiteSpace(value))
                {
                    // 빈 값이면 기본값 587로 설정 (TLS SMTP)
                    SmtpPort = 587;
                }
                else if (int.TryParse(value, out var port))
                {
                    if (port > 0 && port <= 65535)
                    {
                        SmtpPort = port;
                    }
                    else
                    {
                        // 포트 범위를 벗어나면 기본값 587로 설정
                        SmtpPort = 587;
                    }
                }
                else
                {
                    // 숫자가 아닌 값이면 기본값 587로 설정
                    SmtpPort = 587;
                }
                
                // UI 업데이트를 위해 PropertyChanged 이벤트 발생
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// SSL/TLS 사용 여부
        /// </summary>
        public bool UseSsl
        {
            get => _useSsl;
            set => SetProperty(ref _useSsl, value);
        }

        /// <summary>
        /// 사용자 ID
        /// </summary>
        public string UserId
        {
            get => _userId;
            set
            {
                if (SetProperty(ref _userId, value))
                {
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        /// <summary>
        /// 비밀번호
        /// </summary>
        public string Password
        {
            get => _password;
            set => SetProperty(ref _password, value);
        }

        /// <summary>
        /// 새로고침 시간(분)
        /// </summary>
        public int RefreshMinutes
        {
            get => _refreshMinutes;
            set => SetProperty(ref _refreshMinutes, value);
        }

        /// <summary>
        /// 새로고침 간격 텍스트 (TextBox 바인딩용)
        /// </summary>
        public string RefreshMinutesText
        {
            get => _refreshMinutes.ToString();
            set
            {
                // LostFocus 시에만 호출되므로 최종 검증 수행
                if (string.IsNullOrWhiteSpace(value))
                {
                    // 빈 값이면 기본값 5분으로 설정
                    RefreshMinutes = 5;
                }
                else if (int.TryParse(value, out var minutes))
                {
                    if (minutes > 0)
                    {
                        RefreshMinutes = minutes;
                    }
                    else
                    {
                        // 0 이하의 값이면 기본값 5분으로 설정
                        RefreshMinutes = 5;
                    }
                }
                else
                {
                    // 숫자가 아닌 값이면 기본값 5분으로 설정
                    RefreshMinutes = 5;
                }
                
                // UI 업데이트를 위해 PropertyChanged 이벤트 발생
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 이메일 웹사이트 주소 (선택)
        /// </summary>
        public string MailWebUrl
        {
            get => _mailWebUrl;
            set => SetProperty(ref _mailWebUrl, value);
        }

        /// <summary>
        /// UI에서 Expander 확장 상태 (첫 번째 계정만 true)
        /// </summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (SetProperty(ref _isExpanded, value) && !_suppressExpandedEvent)
                {
                    // 확장 상태 변경 시 이벤트 발생
                    ExpandedChanged?.Invoke(this);
                }
            }
        }

        /// <summary>
        /// Expander 확장 상태 변경 이벤트
        /// </summary>
        public event Action<MailAccountViewModel>? ExpandedChanged;

        /// <summary>
        /// 이벤트 발생 없이 IsExpanded 값 설정
        /// </summary>
        public void SetIsExpandedSilently(bool value)
        {
            _suppressExpandedEvent = true;
            try
            {
                IsExpanded = value;
            }
            finally
            {
                _suppressExpandedEvent = false;
            }
        }

        /// <summary>
        /// 계정 활성화 여부
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (SetProperty(ref _isEnabled, value))
                {
                    // IsEnabled 변경 시 즉시 저장 요청
                    EnabledChanged?.Invoke(this);
                }
            }
        }

        /// <summary>
        /// IsEnabled 값이 변경되었을 때 발생하는 이벤트
        /// </summary>
        public event Action<MailAccountViewModel>? EnabledChanged;

        /// <summary>
        /// 계정 이름 (사용자 지정)
        /// </summary>
        public string AccountName
        {
            get => _accountName;
            set
            {
                // 공백 제거
                var trimmedValue = value?.Trim() ?? string.Empty;
                if (SetProperty(ref _accountName, trimmedValue))
                {
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        /// <summary>
        /// 표시 이름 (UI 표시용)
        /// </summary>
        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(AccountName))
                {
                    return AccountName;
                }

                if (!string.IsNullOrWhiteSpace(UserId))
                {
                    return $"{UserId} @ {Pop3Server}";
                }

                return Strings.NewAccount;
            }
        }

        /// <summary>
        /// 편집 모드 여부
        /// </summary>
        public bool IsEditMode
        {
            get => _isEditMode;
            private set => SetProperty(ref _isEditMode, value);
        }

        /// <summary>
        /// 메일 확인 오류 상태
        /// </summary>
        public bool HasError
        {
            get => _hasError;
            set => SetProperty(ref _hasError, value);
        }

        /// <summary>
        /// 오류 메시지
        /// </summary>
        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        /// <summary>
        /// 새로 추가된 계정인지 여부 (아직 저장된 적 없음)
        /// </summary>
        public bool IsNewAccount => _isNewAccount;

        /// <summary>
        /// 편집 모드 시작 (현재 값 백업)
        /// </summary>
        public void BeginEdit()
        {
            _backup = AccountBackup.CreateFrom(this);
            IsEditMode = true;
        }

        /// <summary>
        /// 편집 모드 취소 (백업된 값으로 복원)
        /// </summary>
        /// <summary>
        /// 편집 모드 취소 (백업된 값으로 복원)
        /// </summary>
        public void CancelEdit()
        {
            if (_backup != null)
            {
                _backup.RestoreTo(this);

                // 모든 속성 변경 알림
                OnPropertyChanged(nameof(Pop3Server));
                OnPropertyChanged(nameof(Pop3PortText));
                OnPropertyChanged(nameof(SmtpServer));
                OnPropertyChanged(nameof(SmtpPortText));
                OnPropertyChanged(nameof(UseSsl));
                OnPropertyChanged(nameof(UserId));
                OnPropertyChanged(nameof(Password));
                OnPropertyChanged(nameof(RefreshMinutesText));
                OnPropertyChanged(nameof(MailWebUrl));
                OnPropertyChanged(nameof(IsEnabled));
                OnPropertyChanged(nameof(AccountName));
                OnPropertyChanged(nameof(DisplayName));
            }

            IsEditMode = false;
            _backup = null;
        }

        /// <summary>
        /// 편집 모드 종료 (저장)
        /// </summary>
        public void EndEdit()
        {
            IsEditMode = false;
            _isNewAccount = false;
            _backup = null; // 백업 정리
        }

        /// <summary>
        /// 필수 입력 값 검증
        /// </summary>
        public bool HasRequiredValues()
        {
            return !string.IsNullOrWhiteSpace(Pop3Server)
                && !string.IsNullOrWhiteSpace(UserId)
                && !string.IsNullOrWhiteSpace(Password)
                && !string.IsNullOrWhiteSpace(AccountName?.Trim())
                && RefreshMinutes > 0;
        }

        /// <summary>
        /// 누락된 필수 입력 항목 목록 반환
        /// </summary>
        public List<string> GetMissingRequiredFields()
        {
            var missingFields = new List<string>();

            if (string.IsNullOrWhiteSpace(AccountName?.Trim()))
            {
                missingFields.Add(Strings.FieldAccountName);
            }

            if (string.IsNullOrWhiteSpace(Pop3Server))
            {
                missingFields.Add(Strings.FieldPop3Server);
            }

            if (string.IsNullOrWhiteSpace(UserId))
            {
                missingFields.Add(Strings.FieldUserId);
            }

            if (string.IsNullOrWhiteSpace(Password))
            {
                missingFields.Add(Strings.FieldPassword);
            }

            if (RefreshMinutes <= 0)
            {
                missingFields.Add(Strings.FieldSyncInterval);
            }

            return missingFields;
        }

        /// <summary>
        /// 오류 상태 설정
        /// </summary>
        public void SetError(string errorMessage)
        {
            HasError = true;
            ErrorMessage = errorMessage;
        }

        /// <summary>
        /// 오류 상태 해제
        /// </summary>
        public void ClearError()
        {
            HasError = false;
            ErrorMessage = string.Empty;
        }

        /// <summary>
        /// MailSettings로 변환
        /// </summary>
        public MailSettings ToMailSettings()
        {
            return new MailSettings
            {
                Pop3Server = Pop3Server.Trim(),
                Pop3Port = Pop3Port,
                SmtpServer = SmtpServer.Trim(),
                SmtpPort = SmtpPort,
                UseSsl = UseSsl,
                UserId = UserId.Trim(),
                Password = Password,
                RefreshMinutes = RefreshMinutes > 0 ? RefreshMinutes : 5,
                MailWebUrl = MailWebUrl.Trim(),
                IsEnabled = IsEnabled,
                AccountName = AccountName?.Trim() ?? string.Empty
            };
        }
    }
}
