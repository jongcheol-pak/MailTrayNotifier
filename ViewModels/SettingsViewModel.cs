using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MailTrayNotifier.Constants;
using MailTrayNotifier.Models;
using MailTrayNotifier.Services;
using MailTrayNotifier.Resources;
using Microsoft.Win32;
using Microsoft.UI.Dispatching;
using MailTrayNotifier.WinUI;
using MailTrayNotifier.WinUI.Dialogs;
using MailTrayNotifier.WinUI.Theming;

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
        private readonly UpdateCheckService _updateCheckService;
        // UI 스레드 마샬링 (생성자가 UI 스레드에서 호출됨)
        private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "MailTrayNotifier";

        // 자동 실행 최초 1회 기본 등록 여부 마커 (앱 전용 레지스트리 키)
        private const string AppSettingsKeyPath = @"Software\MailTrayNotifier";
        private const string AutoStartInitializedValue = "AutoStartInitialized";

        /// <summary>
        /// 저장 성공 시 창 닫기 요청 이벤트
        /// </summary>
        public event Action? CloseRequested;

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

        internal SettingsViewModel(
            SettingsService settingsService,
            MailPollingService mailPollingService,
            MailClientService mailClientService,
            MailStateStore mailStateStore,
            UpdateCheckService updateCheckService)
        {
            _settingsService = settingsService;
            _mailPollingService = mailPollingService;
            _mailClientService = mailClientService;
            _mailStateStore = mailStateStore;
            _updateCheckService = updateCheckService;

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
                if (SetProperty(ref _isRefreshEnabled, value))
                {
                    // 각 계정 아이콘 표시 조건에 반영
                    foreach (var account in Accounts)
                    {
                        account.IsRefreshEnabled = value;
                    }

                    if (_isInitialized)
                    {
                        _ = SaveIsRefreshEnabledAsync(value);
                    }
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

        /// <summary>
        /// 최초 실행 시 1회 자동 실행을 기본 등록한다.
        /// 초기화 마커가 이미 있으면(이후 사용자가 끈 상태 포함) 아무 동작도 하지 않는다.
        /// </summary>
        public static void EnsureFirstRunAutoStartRegistration()
        {
            using var appKey = Registry.CurrentUser.CreateSubKey(AppSettingsKeyPath);
            // 마커가 이미 있으면 사용자의 현재 설정을 존중하여 건너뛴다.
            if (appKey is null || appKey.GetValue(AutoStartInitializedValue) != null)
            {
                return;
            }

            // 실행 경로 미확인 시 등록/마커 모두 건너뛰어 다음 실행에서 재시도한다.
            var exePath = Environment.ProcessPath;
            if (exePath is null)
            {
                return;
            }

            // 최초 1회: 자동 실행 등록
            using (var runKey = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true))
            {
                runKey?.SetValue(AppName, exePath);
            }

            // 초기화 완료 마커 기록 (이후 사용자가 끈 상태를 존중)
            appKey.SetValue(AutoStartInitializedValue, 1, RegistryValueKind.DWord);
        }

        public string AppVersion
        {
            get
            {
                try
                {
                    // MSIX 패키지 버전 (Package.appxmanifest의 Version)
                    var v = Windows.ApplicationModel.Package.Current.Id.Version;
                    return $"{v.Major}.{v.Minor}.{v.Build}";
                }
                catch
                {
                    // 비패키지 실행 시 어셈블리 버전으로 대체
                    var asm = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                    return asm is null ? string.Empty : $"{asm.Major}.{asm.Minor}.{asm.Build}";
                }
            }
        }

        /// <summary>정보 화면 버전 표시 텍스트 (예: "버전: 1.7.0")</summary>
        public string AppVersionText => $"{Strings.VersionLabel}{AppVersion}";

        /// <summary>앱 공식 홈페이지 URL</summary>
        public string OfficialWebsiteUrl => "https://jongcheol-pak.github.io/MailTrayNotifier/";

        /// <summary>앱 이름 (정보 화면 표시용)</summary>
        public string AppDisplayName => Strings.AppName;

        // WinUI 3(WinRT)에서는 [ObservableProperty] 필드가 AOT 비호환(MVVMTK0045)이라 수동 프로퍼티 사용
        private string _latestVersion = string.Empty;
        public string LatestVersion
        {
            get => _latestVersion;
            set => SetProperty(ref _latestVersion, value);
        }

        private bool _isUpdateAvailable;
        public bool IsUpdateAvailable
        {
            get => _isUpdateAvailable;
            set => SetProperty(ref _isUpdateAvailable, value);
        }

        private string _latestDownloadUrl = string.Empty;

        /// <summary>
        /// GitHub Releases에서 최신 버전을 확인한다.
        /// </summary>
        public async Task CheckForUpdateAsync()
        {
            var release = await _updateCheckService.GetLatestReleaseAsync();
            if (release is null)
            {
                return;
            }

            var currentVersionString = AppVersion;
            if (!Version.TryParse(currentVersionString, out var currentVersion))
            {
                return;
            }

            if (release.Version > currentVersion)
            {
                LatestVersion = $"{release.Version.Major}.{release.Version.Minor}.{release.Version.Build}";
                _latestDownloadUrl = release.Url;
                IsUpdateAvailable = true;
            }
        }

        /// <summary>
        /// GitHub 릴리스 페이지를 브라우저에서 연다.
        /// </summary>
        [RelayCommand]
        private void OpenUpdatePage()
        {
            if (!string.IsNullOrEmpty(_latestDownloadUrl))
            {
                Process.Start(new ProcessStartInfo(_latestDownloadUrl) { UseShellExecute = true });
            }
        }

        /// <summary>
        /// 사용 중인 오픈 소스 라이브러리 목록
        /// </summary>
        public IReadOnlyList<OpenSourceLibrary> OpenSourceLibraries { get; } =
        [
            new("Windows App SDK", "MIT License", "https://github.com/microsoft/WindowsAppSDK"),
            new("WinUIEx", "MIT License", "https://github.com/dotMorten/WinUIEx"),
            new("CommunityToolkit.Mvvm", "MIT License", "https://github.com/CommunityToolkit/dotnet"),
            new("CommunityToolkit.WinUI Controls", "MIT License", "https://github.com/CommunityToolkit/Windows"),
            new("MailKit", "MIT License", "https://github.com/jstedfast/MailKit"),
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
                accountVm.IsRefreshEnabled = IsRefreshEnabled;
                // 모든 계정을 접힌 상태로 초기화 (이벤트 발생 없이)
                accountVm.SetIsExpandedSilently(false);
                // 기존 계정은 편집 모드 종료 상태로
                accountVm.EndEdit();
                SubscribeAccountEvents(accountVm);
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
                MessageBox.Show(
                    string.Format(Strings.MaxAccountsReached, MailConstants.MaxAccounts),
                    Strings.MaxAccountsTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var newAccount = new MailAccountViewModel
            {
                IsExpanded = true,  // 새 계정은 펼쳐진 상태로
                IsRefreshEnabled = IsRefreshEnabled
            };

            SubscribeAccountEvents(newAccount);

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

            var result = MessageBox.Show(
                string.Format(Strings.DeleteAccountConfirm, account.DisplayName),
                Strings.DeleteAccountTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            UnsubscribeAccountEvents(account);

            Accounts.Remove(account);
            OnPropertyChanged(nameof(AccountCountText));

            // 기존 계정이 삭제된 경우에만 저장 (새 계정은 아직 저장된 적 없음)
            if (!account.IsNewAccount)
            {
                // 해당 계정의 메일 상태도 삭제
                var accountKey = account.GetAccountKey();
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
        /// 계정 이벤트 구독
        /// </summary>
        private void SubscribeAccountEvents(MailAccountViewModel account)
        {
            account.EnabledChanged += OnAccountEnabledChanged;
            account.ExpandedChanged += OnAccountExpandedChanged;
        }

        /// <summary>
        /// 계정 이벤트 구독 해제
        /// </summary>
        private void UnsubscribeAccountEvents(MailAccountViewModel account)
        {
            account.EnabledChanged -= OnAccountEnabledChanged;
            account.ExpandedChanged -= OnAccountExpandedChanged;
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
                MessageBox.Show(
                    nameValidationError,
                    Strings.AccountNameError,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            // 필수 입력 값 검증
            var missingFields = account.GetMissingRequiredFields();
            if (missingFields.Count > 0)
            {
                MessageBox.Show(
                    $"{Strings.MissingFieldsMessage}\n\n• {string.Join("\n• ", missingFields)}",
                    Strings.InputError,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            // 웹사이트 주소 검증 (선택 사항이지만 입력한 경우 유효성 검사)
            if (!string.IsNullOrWhiteSpace(account.MailWebUrl))
            {
                if (!Uri.TryCreate(account.MailWebUrl, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    MessageBox.Show(
                        Strings.InvalidMailWebUrl,
                        Strings.InputError,
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
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
        private async Task SaveAllAccountsAsync(bool includeIncomplete = false)
        {
            var accountsQuery = includeIncomplete
                ? Accounts.AsEnumerable()
                : Accounts.Where(a => a.HasRequiredValues());

            var collection = new MailSettingsCollection
            {
                IsRefreshEnabled = IsRefreshEnabled,
                Language = _selectedLanguageCode,
                Theme = _selectedThemeCode,
                Accounts = accountsQuery
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
                UnsubscribeAccountEvents(account);
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
                UnsubscribeAccountEvents(account);
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
                MessageBox.Show(
                    validationError,
                    Strings.InputError,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
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
                    var accountViewModel = Accounts.FirstOrDefault(a => a.GetAccountKey() == account.GetAccountKey());

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
                    var result = MessageBox.Show(
                        string.Format(Strings.ConnectionErrorMessage, string.Join("\n", testResults)),
                        Strings.ConnectionErrorTitle,
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    // 사용자가 아니요를 선택하면 저장 중단
                    if (result == MessageBoxResult.No)
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

        /// <summary>
        /// 계정 초기화: 등록된 모든 계정과 알림 메일 정보 삭제 (테마/언어 설정은 유지)
        /// </summary>
        [RelayCommand]
        private void ResetAccounts()
        {
            var result = MessageBox.Show(
                Strings.ResetConfirmMessage,
                Strings.ResetConfirmTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            // 폴링 중지
            _mailPollingService.Stop();

            // 설정 파일 삭제 (계정 정보)
            _settingsService.Clear();

            // 메일 상태 파일 삭제 (알림 메일 정보)
            _mailStateStore.Clear();

            // 화면 계정 목록 초기화 (테마/언어는 유지)
            _isInitialized = false;
            IsRefreshEnabled = true;
            Accounts.Clear();
            _isInitialized = true;

            OnPropertyChanged(nameof(AccountCountText));

            MessageBox.Show(
                Strings.ResetCompleted,
                Strings.AlertTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        /// <summary>
        /// 알림 메일 초기화: 계정은 유지하고 모든 계정의 알림 메일 정보만 삭제.
        /// 폴링을 중지하고 상태를 비운 뒤, 조건(유효 계정·알림 ON) 충족 시 폴링을 재시작한다.
        /// 초기화 후에는 서버에 남은 메일이 다시 알림될 수 있다.
        /// </summary>
        [RelayCommand]
        private void ClearAllMailStates()
        {
            var result = MessageBox.Show(
                Strings.ClearMailStatesConfirmMessage,
                Strings.ClearMailStatesConfirmTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            // 폴링 중지 → 알림 메일 상태 전체 삭제 → 재시작(Start 내부 조건 충족 시에만 동작)
            _mailPollingService.Stop();
            _mailStateStore.Clear();
            _mailPollingService.Start();

            MessageBox.Show(
                Strings.ClearMailStatesCompleted,
                Strings.AlertTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
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
            return Accounts.FirstOrDefault(a => a.GetAccountKey() == accountKey);
        }

        /// <summary>
        /// 메일 폴링 서비스에서 계정 오류 발생 시 호출
        /// </summary>
        private void OnAccountErrorOccurred(string accountKey, string errorMessage)
        {
            // UI 스레드에서 실행
            _dispatcherQueue.TryEnqueue(() => SetAccountError(accountKey, errorMessage));
        }

        /// <summary>
        /// 메일 폴링 서비스에서 계정 오류 해제 시 호출
        /// </summary>
        private void OnAccountErrorCleared(string accountKey)
        {
            // UI 스레드에서 실행
            _dispatcherQueue.TryEnqueue(() => ClearAccountError(accountKey));
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
        /// 언어 변경 처리 (설정 저장만 — 실제 적용은 앱 재시작 시 ApplyStartupSettings에서 수행)
        /// </summary>
        private async Task ChangeLanguageAsync(string languageCode)
        {
            _isChangingLanguage = true;
            try
            {
                var collection = await _settingsService.LoadCollectionAsync();
                collection.Language = languageCode;
                await _settingsService.SaveCollectionAsync(collection);
            }
            finally
            {
                _isChangingLanguage = false;
            }
        }

        /// <summary>
        /// 언어 코드에 따른 CurrentUICulture 적용 (앱 시작 시 1회 호출, 코드 문자열 .resx 담당)
        /// </summary>
        /// <remarks>
        /// .resw(x:Uid) 리소스용 PrimaryLanguageOverride는 XAML 로드 전에 설정해야 하므로
        /// Program.Main에서 처리한다(여기서 설정하면 ResourceContext 고정 후라 한 박자 늦게 적용됨).
        /// </remarks>
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
                _dispatcherQueue.TryEnqueue(() => ApplyTheme(themeCode));
            }
            finally
            {
                _isChangingTheme = false;
            }
        }

        /// <summary>
        /// 테마 코드("dark"/"light"/그 외=시스템)에 따라 WinUI 테마 적용
        /// </summary>
        public static void ApplyTheme(string themeCode)
        {
            ThemeHelper.Apply(themeCode);
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
                UnsubscribeAccountEvents(account);
            }
        }
    }
}
