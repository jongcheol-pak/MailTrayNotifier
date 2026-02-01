using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MailTrayNotifier.Models;
using MailTrayNotifier.Services;
using Microsoft.Win32;

namespace MailTrayNotifier.ViewModels
{
    /// <summary>
    /// 설정 화면 ViewModel
    /// </summary>
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly SettingsService _settingsService;
        private readonly MailPollingService _mailPollingService;
        private readonly MailClientService _mailClientService;
        private readonly MailStateStore _mailStateStore;
        private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "MailTrayNotifier";

        /// <summary>
        /// 저장 성공 시 창 닫기 요청 이벤트
        /// </summary>
        public event Action? CloseRequested;

        private bool _isInitialized;

        public SettingsViewModel(
            SettingsService settingsService,
            MailPollingService mailPollingService,
            MailClientService mailClientService,
            MailStateStore mailStateStore)
        {
            _settingsService = settingsService;
            _mailPollingService = mailPollingService;
            _mailClientService = mailClientService;
            _mailStateStore = mailStateStore;
        }

        private bool _isRefreshEnabled = true;
        private string _pop3Server = string.Empty;
        private int _pop3Port = 995;
        private string _smtpServer = string.Empty;
        private int _smtpPort = 465;
        private bool _useSsl = true;
        private string _userId = string.Empty;
        private string _password = string.Empty;
        private int _refreshMinutes = 5;
        private string _mailWebUrl = string.Empty;

        /// <summary>
        /// 새로고침 기능 사용 여부 (변경 시 즉시 저장 및 적용)
        /// </summary>
        public bool IsRefreshEnabled
        {
            get => _isRefreshEnabled;
            set
            {
                if (SetProperty(ref _isRefreshEnabled, value) && _isInitialized)
                {
                    _ = SaveIsRefreshEnabledAsync(value);
                }
            }
        }

        /// <summary>
        /// IsRefreshEnabled 값만 즉시 저장 및 적용
        /// </summary>
        private async Task SaveIsRefreshEnabledAsync(bool isEnabled)
        {
            var settings = await _settingsService.LoadAsync();
            settings.IsRefreshEnabled = isEnabled;
            await _settingsService.SaveAsync(settings);
            _mailPollingService.ApplySettings(settings);
        }

        public string Pop3Server
        {
            get => _pop3Server;
            set => SetProperty(ref _pop3Server, value);
        }

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
                if (int.TryParse(value, out var port) && port > 0)
                {
                    Pop3Port = port;
                }
                OnPropertyChanged();
            }
        }

        public string SmtpServer
        {
            get => _smtpServer;
            set => SetProperty(ref _smtpServer, value);
        }

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
                if (int.TryParse(value, out var port) && port > 0)
                {
                    SmtpPort = port;
                }
                OnPropertyChanged();
            }
        }

        public bool UseSsl
        {
            get => _useSsl;
            set => SetProperty(ref _useSsl, value);
        }

        public string UserId
        {
            get => _userId;
            set => SetProperty(ref _userId, value);
        }

        public string Password
        {
            get => _password;
            set => SetProperty(ref _password, value);
        }

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
                if (int.TryParse(value, out var minutes) && minutes > 0)
                {
                    RefreshMinutes = minutes;
                }
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

        public bool RunAtStartup
        {
            get
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
                return key?.GetValue(AppName) != null;
            }
            set
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true);
                if (value)
                {
                    var exePath = Environment.ProcessPath;
                    if (exePath != null)
                    {
                        key?.SetValue(AppName, exePath);
                    }
                }
                else
                {
                    key?.DeleteValue(AppName, false);
                }
                OnPropertyChanged();
            }
        }

        public string AppVersion
        {
            get
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
            }
        }

        public string LicenseInfo => 
@"CommunityToolkit.Mvvm (MIT)
Hardcodet.NotifyIcon.Wpf (MIT)
Microsoft.Toolkit.Uwp.Notifications (MIT)
MailKit (MIT)
WPF-UI (MIT)";

        public async Task InitializeAsync()
        {
            var settings = await _settingsService.LoadAsync();
            IsRefreshEnabled = settings.IsRefreshEnabled;
            Pop3Server = settings.Pop3Server;
            Pop3Port = settings.Pop3Port;
            SmtpServer = settings.SmtpServer;
            SmtpPort = settings.SmtpPort;
            UseSsl = settings.UseSsl;
            UserId = settings.UserId;
            Password = settings.Password;
            RefreshMinutes = settings.RefreshMinutes;
            MailWebUrl = settings.MailWebUrl;
            
            _isInitialized = true;
            
            // Notify startup property might have changed externally or just to be sure UI syncs
            OnPropertyChanged(nameof(RunAtStartup));
        }

        /// <summary>
        /// 필수 입력 값 검증
        /// </summary>
        private string? ValidateRequiredFields()
        {
            var missingFields = new List<string>();

            if (string.IsNullOrWhiteSpace(Pop3Server))
            {
                missingFields.Add("POP3 서버");
            }

            if (string.IsNullOrWhiteSpace(UserId))
            {
                missingFields.Add("아이디");
            }

            if (string.IsNullOrWhiteSpace(Password))
            {
                missingFields.Add("비밀번호");
            }

            if (RefreshMinutes <= 0)
            {
                missingFields.Add("동기화 시간");
            }

            // 웹사이트 주소 검증 (선택 사항이지만 입력한 경우 유효성 검사)
            if (!string.IsNullOrWhiteSpace(MailWebUrl))
            {
                if (!Uri.TryCreate(MailWebUrl, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    return "이메일 사이트 주소가 올바르지 않습니다.\n\n예: https://mail.example.com";
                }
            }

            if (missingFields.Count > 0)
            {
                return $"다음 항목을 입력해주세요:\n\n• {string.Join("\n• ", missingFields)}";
            }

            return null;
        }

        [RelayCommand]
        private async Task SaveAsync()
        {
            // 필수 입력 값 검증
            var validationError = ValidateRequiredFields();
            if (validationError is not null)
            {
                System.Windows.MessageBox.Show(
                    validationError,
                    "입력 오류",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            var normalizedMinutes = RefreshMinutes <= 0 ? 1 : RefreshMinutes;
            RefreshMinutes = normalizedMinutes;
            // Update text property if it was invalid
            OnPropertyChanged(nameof(RefreshMinutesText));

            var settings = new MailSettings
                        {
                            IsRefreshEnabled = IsRefreshEnabled,
                            Pop3Server = Pop3Server.Trim(),
                            Pop3Port = Pop3Port,
                            SmtpServer = SmtpServer.Trim(),
                            SmtpPort = SmtpPort,
                            UseSsl = UseSsl,
                            UserId = UserId.Trim(),
                            Password = Password,
                            RefreshMinutes = normalizedMinutes,
                            MailWebUrl = MailWebUrl.Trim()
                        };

                        // 새로고침이 활성화된 경우에만 메일 서버 접속 테스트
                        if (IsRefreshEnabled)
                        {
                            try
                            {
                                await _mailClientService.TestConnectionAsync(settings);
                            }
                            catch (Exception ex)
                            {
                                System.Windows.MessageBox.Show(
                                    $"메일 서버 접속에 실패했습니다.\n\n{ex.Message}",
                                    "접속 오류",
                                    System.Windows.MessageBoxButton.OK,
                                    System.Windows.MessageBoxImage.Error);
                                return;
                            }
                        }

                        await _settingsService.SaveAsync(settings);
                        _mailPollingService.ApplySettings(settings);

                        // 저장 성공 시 창 닫기
                        CloseRequested?.Invoke();
                    }

        [RelayCommand]
        private void Reset()
        {
            var result = System.Windows.MessageBox.Show(
                "모든 설정과 메일 상태를 초기화하시겠습니까?\n\n• 설정값이 모두 삭제됩니다.\n• 읽음 처리된 메일 정보가 삭제됩니다.",
                "초기화 확인",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (result != System.Windows.MessageBoxResult.Yes)
            {
                return;
            }

            // 폴링 중지
            _mailPollingService.Stop();

            // 설정 파일 삭제
            _settingsService.Clear();

            // 메일 상태 파일 삭제
            _mailStateStore.Clear();

            // 화면 설정값 초기화
            _isInitialized = false;
            IsRefreshEnabled = true;
            Pop3Server = string.Empty;
            Pop3Port = 995;
            SmtpServer = string.Empty;
            SmtpPort = 465;
            UseSsl = true;
            UserId = string.Empty;
            Password = string.Empty;
            RefreshMinutes = 5;
            MailWebUrl = string.Empty;
            _isInitialized = true;

            // 포트 텍스트 갱신
            OnPropertyChanged(nameof(Pop3PortText));
            OnPropertyChanged(nameof(SmtpPortText));
            OnPropertyChanged(nameof(RefreshMinutesText));

            System.Windows.MessageBox.Show(
                "초기화 완료",
                "알림",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
                }
            }
