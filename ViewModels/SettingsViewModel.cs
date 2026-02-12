using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MailTrayNotifier.Constants;
using MailTrayNotifier.Models;
using MailTrayNotifier.Services;
using MailTrayNotifier.Resources;
using Microsoft.Win32;
using Wpf.Ui.Appearance;

namespace MailTrayNotifier.ViewModels
{
    /// <summary>
    /// 설정 화면 ViewModel (다중 계정 지원)
    /// </summary>
    public partial class SettingsViewModel : ObservableObject, IDisposable
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

        /// <summary>
        /// 언어 변경 시 UI 갱신 요청 이벤트
        /// </summary>
        public event Action? LanguageChanged;

        private bool _isInitialized;
        private string _selectedLanguageCode = string.Empty;
        private bool _isChangingLanguage;
        private string _selectedThemeCode = string.Empty;
        private bool _isChangingTheme;

        /// <summary>
        /// 사용 가능한 언어 목록
        /// </summary>
        public IReadOnlyList<LanguageOption> AvailableLanguages =>
        [
            new(string.Empty, Strings.LanguageSystemDefault),
            new("en", "English"),
            new("ko", "한국어"),
            new("ja", "日本語"),
            new("zh-CN", "简体中文"),
            new("zh-TW", "繁體中文"),
        ];

        /// <summary>
        /// 선택된 언어 코드 (빈 문자열 = 시스템 기본)
        /// </summary>
        public string SelectedLanguageCode
        {
            get => _selectedLanguageCode;
            set
            {
                // 언어 변경 중 ComboBox 재생성에 의한 재진입 방지
                if (_isChangingLanguage) return;

                var normalized = value ?? string.Empty;
                if (SetProperty(ref _selectedLanguageCode, normalized) && _isInitialized)
                {
                    _ = ChangeLanguageAsync(normalized).ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                        {
                            Debug.WriteLine($"언어 변경 실패: {t.Exception?.GetBaseException().Message}");
                        }
                    }, TaskScheduler.Default);
                }
            }
        }

        /// <summary>
        /// 사용 가능한 테마 목록
        /// </summary>
        public IReadOnlyList<ThemeOption> AvailableThemes =>
        [
            new(string.Empty, Strings.ThemeSystemDefault),
            new("dark", Strings.ThemeDark),
            new("light", Strings.ThemeLight),
        ];

        /// <summary>
        /// 선택된 테마 코드 (빈 문자열 = 시스템 기본)
        /// </summary>
        public string SelectedThemeCode
        {
            get => _selectedThemeCode;
            set
            {
                // 테마 변경 중 ComboBox 재생성에 의한 재진입 방지
                if (_isChangingTheme) return;

                var normalized = value ?? string.Empty;
                if (SetProperty(ref _selectedThemeCode, normalized) && _isInitialized)
                {
                    _ = ChangeThemeAsync(normalized).ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                        {
                            Debug.WriteLine($"테마 변경 실패: {t.Exception?.GetBaseException().Message}");
                        }
                    }, TaskScheduler.Default);
                }
            }
        }

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

            // 메일 폴링 서비스의 계정별 오류 이벤트 구독
            _mailPollingService.AccountErrorOccurred += OnAccountErrorOccurred;
            _mailPollingService.AccountErrorCleared += OnAccountErrorCleared;
        }

        private bool _isRefreshEnabled = true;

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
        /// 메일 계정 목록
        /// </summary>
        public ObservableCollection<MailAccountViewModel> Accounts { get; } = new();

        /// <summary>
        /// 계정 개수 표시 텍스트 (예: "3/{MailConstants.MaxAccounts}")
        /// </summary>
        public string AccountCountText => $"{Accounts.Count}/{MailConstants.MaxAccounts}";

        /// <summary>
        /// IsRefreshEnabled 값만 즉시 저장 및 적용 (폴링 시작/중지 포함)
        /// </summary>
        private async Task SaveIsRefreshEnabledAsync(bool isEnabled)
        {
            var collection = await _settingsService.LoadCollectionAsync();
            collection.IsRefreshEnabled = isEnabled;
            await _settingsService.SaveCollectionAsync(collection);
            _mailPollingService.ApplySettings(collection);
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

        /// <summary>
        /// 사용 중인 오픈 소스 라이브러리 목록
        /// </summary>
        public IReadOnlyList<OpenSourceLibrary> OpenSourceLibraries { get; } =
        [
            new("CommunityToolkit.Mvvm", "MIT", "https://github.com/CommunityToolkit/dotnet"),
            new("Hardcodet.NotifyIcon.Wpf", "MIT", "https://github.com/hardcodet/wpf-notifyicon"),
            new("Microsoft.Toolkit.Uwp.Notifications", "MIT", "https://github.com/CommunityToolkit/WindowsCommunityToolkit"),
            new("MailKit", "MIT", "https://github.com/jstedfast/MailKit"),
            new("WPF-UI", "MIT", "https://github.com/lepoco/wpfui"),
        ];

        /// <summary>
        /// 오픈 소스 라이브러리 홈페이지 열기
        /// </summary>
        [RelayCommand]
        private void OpenLicenseUrl(string url)
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        public async Task InitializeAsync()
        {
            var collection = await _settingsService.LoadCollectionAsync();
            IsRefreshEnabled = collection.IsRefreshEnabled;
            _selectedLanguageCode = collection.Language;
            OnPropertyChanged(nameof(SelectedLanguageCode));
            _selectedThemeCode = collection.Theme;
            OnPropertyChanged(nameof(SelectedThemeCode));

            Accounts.Clear();
            for (int i = 0; i < collection.Accounts.Count; i++)
            {
                var accountVm = new MailAccountViewModel(collection.Accounts[i]);
                // 모든 계정을 접힌 상태로 초기화 (이벤트 발생 없이)
                accountVm.SetIsExpandedSilently(false);
                // 기존 계정은 편집 모드 종료 상태로
                accountVm.EndEdit();
                // IsEnabled 변경 이벤트 구독
                accountVm.EnabledChanged += OnAccountEnabledChanged;
                // Expander 확장 상태 변경 이벤트 구독
                accountVm.ExpandedChanged += OnAccountExpandedChanged;
                Accounts.Add(accountVm);
            }

            _isInitialized = true;

            OnPropertyChanged(nameof(AccountCountText));

            // Notify startup property might have changed externally or just to be sure UI syncs
            OnPropertyChanged(nameof(RunAtStartup));
        }

        /// <summary>
        /// 계정 추가 명령
        /// </summary>
        [RelayCommand]
        private void AddAccount()
        {
            if (Accounts.Count >= MailConstants.MaxAccounts)
            {
                System.Windows.MessageBox.Show(
                    string.Format(Strings.MaxAccountsReached, MailConstants.MaxAccounts),
                    Strings.MaxAccountsTitle,
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return;
            }

            var newAccount = new MailAccountViewModel
            {
                IsExpanded = true  // 새 계정은 펼쳐진 상태로
            };

            // IsEnabled 변경 이벤트 구독
            newAccount.EnabledChanged += OnAccountEnabledChanged;
            // Expander 확장 상태 변경 이벤트 구독  
            newAccount.ExpandedChanged += OnAccountExpandedChanged;

            // 다른 계정들은 모두 접기 (이벤트 발생 없이)
            foreach (var account in Accounts)
            {
                account.SetIsExpandedSilently(false);
            }

            Accounts.Add(newAccount);
            // 새 계정을 펼친 상태로 설정 (이미 IsExpanded = true로 기본 설정됨)
            OnPropertyChanged(nameof(AccountCountText));
        }

        /// <summary>
        /// 계정 삭제 명령
        /// </summary>
        [RelayCommand]
        private void RemoveAccount(MailAccountViewModel? account)
        {
            if (account is null)
            {
                return;
            }

            var result = System.Windows.MessageBox.Show(
                string.Format(Strings.DeleteAccountConfirm, account.DisplayName),
                Strings.DeleteAccountTitle,
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);

            if (result != System.Windows.MessageBoxResult.Yes)
            {
                return;
            }

            // 이벤트 구독 해제
            account.EnabledChanged -= OnAccountEnabledChanged;
            account.ExpandedChanged -= OnAccountExpandedChanged;

            Accounts.Remove(account);
            OnPropertyChanged(nameof(AccountCountText));

            // 기존 계정이 삭제된 경우에만 저장 (새 계정은 아직 저장된 적 없음)
            if (!account.IsNewAccount)
            {
                // 해당 계정의 메일 상태도 삭제
                var accountKey = account.ToMailSettings().GetAccountKey();
                _mailStateStore.ClearAccount(accountKey);

                _ = SaveAllAccountsAsync().ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        System.Diagnostics.Debug.WriteLine($"계정 삭제 후 저장 실패: {t.Exception?.GetBaseException().Message}");
                    }
                }, TaskScheduler.Default);
            }
        }

        /// <summary>
        /// 개별 계정 저장 (검증 후)
        /// </summary>
        public async Task<bool> SaveAccountAsync(MailAccountViewModel account)
        {
            // 계정 이름 공백 제거
            if (!string.IsNullOrWhiteSpace(account.AccountName))
            {
                account.AccountName = account.AccountName.Trim();
            }

            // 계정 이름 중복 확인
            var nameValidationError = ValidateAccountName(account.AccountName, account);
            if (nameValidationError != null && nameValidationError != Strings.AccountNameTrimmed)
            {
                System.Windows.MessageBox.Show(
                    nameValidationError,
                    Strings.AccountNameError,
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return false;
            }

            // 필수 입력 값 검증
            var missingFields = account.GetMissingRequiredFields();
            if (missingFields.Count > 0)
            {
                System.Windows.MessageBox.Show(
                    $"{Strings.MissingFieldsMessage}\n\n• {string.Join("\n• ", missingFields)}",
                    Strings.InputError,
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return false;
            }

            // 웹사이트 주소 검증 (선택 사항이지만 입력한 경우 유효성 검사)
            if (!string.IsNullOrWhiteSpace(account.MailWebUrl))
            {
                if (!Uri.TryCreate(account.MailWebUrl, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    System.Windows.MessageBox.Show(
                        Strings.InvalidMailWebUrl,
                        Strings.InputError,
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                    return false;
                }
            }

            // 편집 모드 종료
            account.EndEdit();

            // 모든 계정을 settings.json에 저장
            await SaveAllAccountsAsync();

            return true;
        }

        /// <summary>
        /// 모든 계정을 settings.json에 저장
        /// </summary>
        private async Task SaveAllAccountsAsync()
        {
            var collection = new MailSettingsCollection
            {
                IsRefreshEnabled = IsRefreshEnabled,
                Language = _selectedLanguageCode,
                Theme = _selectedThemeCode,
                Accounts = Accounts
                    .Where(a => a.HasRequiredValues())
                    .Select(a => a.ToMailSettings())
                    .ToList()
            };

            await _settingsService.SaveCollectionAsync(collection);
            _mailPollingService.ApplySettings(collection);
        }

        /// <summary>
        /// 계정 편집 취소 처리
        /// </summary>
        public void CancelAccountEdit(MailAccountViewModel account)
        {
            if (account.IsNewAccount)
            {
                // 새 계정인 경우 목록에서 제거
                account.EnabledChanged -= OnAccountEnabledChanged;
                Accounts.Remove(account);
                OnPropertyChanged(nameof(AccountCountText));
            }
            else
            {
                // 기존 계정인 경우 원래 값으로 복원
                account.CancelEdit();
            }
        }

        /// <summary>
        /// 미저장 신규 계정 제거 (창 닫기 시 호출)
        /// </summary>
        public void RemoveUnsavedAccounts()
        {
            var unsaved = Accounts.Where(a => a.IsNewAccount).ToList();
            foreach (var account in unsaved)
            {
                account.EnabledChanged -= OnAccountEnabledChanged;
                account.ExpandedChanged -= OnAccountExpandedChanged;
                Accounts.Remove(account);
            }

            if (unsaved.Count > 0)
            {
                OnPropertyChanged(nameof(AccountCountText));
            }
        }

        /// <summary>
        /// 계정의 활성화 상태 변경 시 호출 (즉시 저장)
        /// </summary>
        private void OnAccountEnabledChanged(MailAccountViewModel account)
        {
            // 새 계정이 아닌 경우에만 즉시 저장
            if (!account.IsNewAccount && _isInitialized)
            {
                System.Diagnostics.Debug.WriteLine($"ToggleSwitch changed for account {account.DisplayName}: {account.IsEnabled}");
                _ = SaveAllAccountsAsync().ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        System.Diagnostics.Debug.WriteLine($"계정 활성화 상태 저장 실패: {t.Exception?.GetBaseException().Message}");
                    }
                }, TaskScheduler.Default);
            }
        }

        /// <summary>
        /// 필수 입력 값 검증
        /// </summary>
        private string? ValidateRequiredFields()
        {
            var validAccounts = Accounts.Where(a => a.HasRequiredValues()).ToList();

            if (validAccounts.Count == 0)
            {
                return Strings.MinOneAccount;
            }

            var errors = new List<string>();

            // 계정 이름 중복 검사 먼저 수행
            var accountNamesUsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < Accounts.Count; i++)
            {
                var account = Accounts[i];

                // 빈 계정은 건너뜀
                if (string.IsNullOrWhiteSpace(account.UserId) &&
                    string.IsNullOrWhiteSpace(account.Pop3Server) &&
                    string.IsNullOrWhiteSpace(account.Password) &&
                    string.IsNullOrWhiteSpace(account.AccountName))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(account.AccountName))
                {
                    var trimmedName = account.AccountName.Trim();
                    if (!accountNamesUsed.Add(trimmedName))
                    {
                        errors.Add(string.Format(Strings.DuplicateAccountName, account.DisplayName, trimmedName));
                    }
                }
            }

            for (int i = 0; i < Accounts.Count; i++)
            {
                var account = Accounts[i];

                // 비어있지 않은 계정만 검증
                if (string.IsNullOrWhiteSpace(account.UserId) &&
                    string.IsNullOrWhiteSpace(account.Pop3Server) &&
                    string.IsNullOrWhiteSpace(account.Password))
                {
                    continue;
                }

                var accountErrors = new List<string>();

                if (string.IsNullOrWhiteSpace(account.Pop3Server))
                {
                    accountErrors.Add(Strings.FieldPop3Server);
                }

                if (string.IsNullOrWhiteSpace(account.UserId))
                {
                    accountErrors.Add(Strings.FieldUserId);
                }

                if (string.IsNullOrWhiteSpace(account.Password))
                {
                    accountErrors.Add(Strings.FieldPassword);
                }

                if (account.RefreshMinutes <= 0)
                {
                    accountErrors.Add(Strings.FieldSyncInterval);
                }

                // 웹사이트 주소 검증 (선택 사항이지만 입력한 경우 유효성 검사)
                if (!string.IsNullOrWhiteSpace(account.MailWebUrl))
                {
                    if (!Uri.TryCreate(account.MailWebUrl, UriKind.Absolute, out var uri) ||
                        (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    {
                        errors.Add(string.Format(Strings.InvalidMailUrlForAccount, i + 1));
                    }
                }

                if (accountErrors.Count > 0)
                {
                    errors.Add($"{string.Format(Strings.MissingFieldsForAccount, account.DisplayName)}\n  • {string.Join("\n  • ", accountErrors)}");
                }
            }

            if (errors.Count > 0)
            {
                return string.Join("\n\n", errors);
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
                    Strings.InputError,
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            var collection = new MailSettingsCollection
            {
                IsRefreshEnabled = IsRefreshEnabled,
                Language = _selectedLanguageCode,
                Theme = _selectedThemeCode,
                Accounts = Accounts
                    .Where(a => a.HasRequiredValues())
                    .Select(a => a.ToMailSettings())
                    .ToList()
            };

            // 새로고침이 활성화된 경우에만 유효한 계정에 대해 메일 서버 접속 테스트
            if (IsRefreshEnabled && collection.Accounts.Count > 0)
            {
                var testResults = new List<string>();

                foreach (var account in collection.Accounts)
                {
                    var accountViewModel = Accounts.FirstOrDefault(a => a.ToMailSettings().GetAccountKey() == account.GetAccountKey());

                    try
                    {
                        await _mailClientService.TestConnectionAsync(account);
                        // 연결 성공 시 오류 상태 해제
                        accountViewModel?.ClearError();
                    }
                    catch (Exception ex)
                    {
                        // 연결 실패 시 오류 상태 설정
                        accountViewModel?.SetError(ex.Message);
                        testResults.Add($"• {account.UserId}@{account.Pop3Server}: {ex.Message}");
                    }
                }

                if (testResults.Count > 0)
                {
                    var result = System.Windows.MessageBox.Show(
                        string.Format(Strings.ConnectionErrorMessage, string.Join("\n", testResults)),
                        Strings.ConnectionErrorTitle,
                        System.Windows.MessageBoxButton.YesNo,
                        System.Windows.MessageBoxImage.Warning);

                    // 사용자가 아니요를 선택하면 저장 중단
                    if (result == System.Windows.MessageBoxResult.No)
                    {
                        return;
                    }
                }
            }

            await _settingsService.SaveCollectionAsync(collection);
            _mailPollingService.ApplySettings(collection);

            // 모든 계정의 편집 모드 종료
            foreach (var account in Accounts)
            {
                account.EndEdit();
            }

            // 저장 성공 시 창 닫기
            CloseRequested?.Invoke();
        }

        [RelayCommand]
        private void Reset()
        {
            var result = System.Windows.MessageBox.Show(
                Strings.ResetConfirmMessage,
                Strings.ResetConfirmTitle,
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
            Accounts.Clear();

            // 테마를 시스템 기본으로 초기화
            _selectedThemeCode = string.Empty;
            OnPropertyChanged(nameof(SelectedThemeCode));
            ApplyTheme(string.Empty);

            // 언어를 시스템 기본으로 초기화
            _selectedLanguageCode = string.Empty;
            OnPropertyChanged(nameof(SelectedLanguageCode));
            ApplyLanguage(string.Empty);

            _isInitialized = true;

            System.Windows.MessageBox.Show(
                Strings.ResetCompleted,
                Strings.AlertTitle,
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);

            // 언어가 변경되었을 수 있으므로 UI 갱신 요청
            LanguageChanged?.Invoke();
        }

        /// <summary>
        /// 특정 계정의 오류 상태 설정
        /// </summary>
        public void SetAccountError(string accountKey, string errorMessage)
        {
            var account = FindAccountByKey(accountKey);
            if (account != null)
            {
                account.SetError(errorMessage);
            }
        }

        /// <summary>
        /// 특정 계정의 오류 상태 해제
        /// </summary>
        public void ClearAccountError(string accountKey)
        {
            var account = FindAccountByKey(accountKey);
            if (account != null)
            {
                account.ClearError();
            }
        }

        /// <summary>
        /// 모든 계정의 오류 상태 해제
        /// </summary>
        public void ClearAllAccountErrors()
        {
            foreach (var account in Accounts)
            {
                account.ClearError();
            }
        }

        /// <summary>
        /// 계정 이름 중복 확인 (대소문자 구분 없이, 공백 제거 후)
        /// </summary>
        /// <param name="accountName">확인할 계정 이름</param>
        /// <param name="excludeAccount">제외할 계정 (수정 시)</param>
        /// <returns>중복되면 true, 아니면 false</returns>
        public bool IsAccountNameDuplicate(string accountName, MailAccountViewModel? excludeAccount = null)
        {
            if (string.IsNullOrWhiteSpace(accountName))
            {
                return false;
            }

            var trimmedName = accountName.Trim();

            return Accounts.Any(account =>
                account != excludeAccount &&
                string.Equals(account.AccountName?.Trim(), trimmedName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 유효한 계정 이름인지 확인 (공백 제거, 중복 확인)
        /// </summary>
        /// <param name="accountName">확인할 계정 이름</param>
        /// <param name="excludeAccount">제외할 계정 (수정 시)</param>
        /// <returns>유효하면 null, 아니면 오류 메시지</returns>
        public string? ValidateAccountName(string accountName, MailAccountViewModel? excludeAccount = null)
        {
            if (string.IsNullOrWhiteSpace(accountName))
            {
                return Strings.EnterAccountName;
            }

            var trimmedName = accountName.Trim();

            if (trimmedName != accountName)
            {
                return Strings.AccountNameTrimmed;
            }

            if (IsAccountNameDuplicate(trimmedName, excludeAccount))
            {
                return Strings.AccountNameDuplicate;
            }

            return null;
        }

        /// <summary>
        /// 계정 키로 계정 ViewModel 찾기
        /// </summary>
        private MailAccountViewModel? FindAccountByKey(string accountKey)
        {
            return Accounts.FirstOrDefault(a => a.ToMailSettings().GetAccountKey() == accountKey);
        }

        /// <summary>
        /// 메일 폴링 서비스에서 계정 오류 발생 시 호출
        /// </summary>
        private void OnAccountErrorOccurred(string accountKey, string errorMessage)
        {
            // UI 스레드에서 실행
            if (System.Windows.Application.Current?.Dispatcher != null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    SetAccountError(accountKey, errorMessage);
                });
            }
        }

        /// <summary>
        /// 메일 폴링 서비스에서 계정 오류 해제 시 호출
        /// </summary>
        private void OnAccountErrorCleared(string accountKey)
        {
            // UI 스레드에서 실행
            if (System.Windows.Application.Current?.Dispatcher != null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    ClearAccountError(accountKey);
                });
            }
        }

        /// <summary>
        /// 계정의 Expander 확장 상태 변경 시 호출 (아코디언 스타일)
        /// </summary>
        private void OnAccountExpandedChanged(MailAccountViewModel expandedAccount)
        {
            // 해당 계정이 확장된 경우에만 다른 계정들을 닫음
            if (expandedAccount.IsExpanded)
            {
                foreach (var account in Accounts)
                {
                    if (account != expandedAccount && account.IsExpanded)
                    {
                        // 이벤트 순환 호출 방지를 위해 조용히 닫기
                        account.SetIsExpandedSilently(false);
                    }
                }
            }
        }

        /// <summary>
        /// 언어 변경 처리 (설정 저장 + 문화 적용 + UI 갱신 요청)
        /// </summary>
        private async Task ChangeLanguageAsync(string languageCode)
        {
            _isChangingLanguage = true;
            try
            {
                var collection = await _settingsService.LoadCollectionAsync();
                collection.Language = languageCode;
                await _settingsService.SaveCollectionAsync(collection);

                ApplyLanguage(languageCode);
                LanguageChanged?.Invoke();
            }
            finally
            {
                _isChangingLanguage = false;
            }
        }

        /// <summary>
        /// 언어 코드에 따른 CurrentUICulture 적용
        /// </summary>
        public static void ApplyLanguage(string languageCode)
        {
            var culture = string.IsNullOrEmpty(languageCode)
                ? App.SystemDefaultCulture
                : new CultureInfo(languageCode);

            Thread.CurrentThread.CurrentUICulture = culture;
            Thread.CurrentThread.CurrentCulture = culture;
            Strings.Culture = culture;
        }

        /// <summary>
        /// 테마 변경 처리 (설정 저장 + 테마 적용)
        /// </summary>
        private async Task ChangeThemeAsync(string themeCode)
        {
            _isChangingTheme = true;
            try
            {
                var collection = await _settingsService.LoadCollectionAsync();
                collection.Theme = themeCode;
                await _settingsService.SaveCollectionAsync(collection);

                // UI 스레드에서 테마 적용
                if (System.Windows.Application.Current?.Dispatcher is { } dispatcher)
                {
                    dispatcher.Invoke(() => ApplyTheme(themeCode));
                }
            }
            finally
            {
                _isChangingTheme = false;
            }
        }

        /// <summary>
        /// 테마 코드에 따른 WPF-UI 테마 적용
        /// </summary>
        public static void ApplyTheme(string themeCode)
        {
            if (themeCode is "dark")
            {
                ApplicationThemeManager.Apply(ApplicationTheme.Dark);
            }
            else if (themeCode is "light")
            {
                ApplicationThemeManager.Apply(ApplicationTheme.Light);
            }
            else
            {
                // 시스템 기본 테마 적용
                var systemTheme = ApplicationThemeManager.GetSystemTheme();
                ApplicationThemeManager.Apply(
                    systemTheme is SystemTheme.Light or SystemTheme.HC1 or SystemTheme.HC2
                        ? ApplicationTheme.Light
                        : ApplicationTheme.Dark);
            }
        }

        /// <summary>
        /// 리소스 해제
        /// </summary>
        public void Dispose()
        {
            // 이벤트 구독 해제
            _mailPollingService.AccountErrorOccurred -= OnAccountErrorOccurred;
            _mailPollingService.AccountErrorCleared -= OnAccountErrorCleared;

            // 계정 이벤트 구독 해제
            foreach (var account in Accounts)
            {
                account.EnabledChanged -= OnAccountEnabledChanged;
                account.ExpandedChanged -= OnAccountExpandedChanged;
            }
        }
    }
}
