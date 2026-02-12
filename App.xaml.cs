using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using MailTrayNotifier.Services;
using MailTrayNotifier.Resources;

namespace MailTrayNotifier
{
    /// <summary>
    /// WPF 앱 진입점
    /// </summary>
    public partial class App : Application
    {
        private const string MutexName = "MailTrayNotifier_SingleInstance_Mutex";
        private static Mutex? _mutex;

        /// <summary>
        /// 시스템 기본 문화 (앱 시작 시 캡처)
        /// </summary>
        public static CultureInfo SystemDefaultCulture { get; } = CultureInfo.CurrentUICulture;

        public static App? Instance { get; private set; }

        private readonly SettingsService _settingsService = new();
        private readonly NotificationService _notificationService = new();
        private readonly MailClientService _mailClientService = new();
        private readonly MailStateStore _mailStateStore = new();
        private readonly MailPollingService _mailPollingService;

        public SettingsService SettingsService => _settingsService;
        public MailPollingService MailPollingService => _mailPollingService;
        public MailClientService MailClientService => _mailClientService;
        public MailStateStore MailStateStore => _mailStateStore;


        private TaskbarIcon? _trayIcon;
        private MainWindow? _window;
        private bool _isExiting;
        private MenuItem? _toggleMenuItem;
        private Separator? _toggleMenuSeparator;
        private MenuItem? _settingsMenuItem;
        private MenuItem? _exitMenuItem;
        private Icon? _startIcon;
        private Icon? _stopIcon;
        private Icon? _warningIcon;
        private bool _hasAccountError;

        public App()
        {
            Instance = this;
            _mailPollingService = new MailPollingService(_mailClientService, _mailStateStore, _notificationService);
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            // 한국어 레거시 인코딩 지원 (EUC-KR, ISO-2022-KR 등)
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // 저장된 언어 설정 적용 (UI 생성 전)
            ApplyStartupLanguage();

            // 중복 실행 방지
            _mutex = new Mutex(true, MutexName, out var isNewInstance);
            if (!isNewInstance)
            {
                MessageBox.Show(Strings.AlreadyRunning, Strings.AlertTitle, MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            base.OnStartup(e);

            // 미처리 예외 핸들러 등록
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            InitializeTray();
            _window = new MainWindow();
            _window.Hide();

            _ = InitializeServicesAsync().ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    Debug.WriteLine($"서비스 초기화 실패: {t.Exception?.GetBaseException().Message}");
                }
            }, TaskScheduler.Default);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            CleanupResources();
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            base.OnExit(e);
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                Debug.WriteLine($"미처리 예외 발생: {ex.GetType().Name}: {ex.Message}");
                Debug.WriteLine($"StackTrace: {ex.StackTrace}");
            }
        }

        private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            Debug.WriteLine($"Dispatcher 미처리 예외: {e.Exception.GetType().Name}: {e.Exception.Message}");
            Debug.WriteLine($"StackTrace: {e.Exception.StackTrace}");
            e.Handled = true;
        }




        /// <summary>
        /// 트레이 아이콘 초기화
        /// </summary>
        private void InitializeTray()
        {
            LoadIcons();

            var contextMenu = new ContextMenu();

            // 초기 상태: 숨김 (새로고침 기능 비활성화 시 숨김)
            _toggleMenuItem = new MenuItem
            {
                Header = Strings.TrayStartPolling,
                IsEnabled = false,
                Visibility = Visibility.Collapsed
            };
            _toggleMenuItem.Click += (_, _) => TogglePolling();
            contextMenu.Items.Add(_toggleMenuItem);

            _toggleMenuSeparator = new Separator { Visibility = Visibility.Collapsed };
            contextMenu.Items.Add(_toggleMenuSeparator);

            var settingsItem = new MenuItem { Header = Strings.TraySettings };
            settingsItem.Click += (_, _) => ShowSettings();
            _settingsMenuItem = settingsItem;
            contextMenu.Items.Add(settingsItem);

            var exitItem = new MenuItem { Header = Strings.TrayExit };
            exitItem.Click += (_, _) => ExitApp();
            _exitMenuItem = exitItem;
            contextMenu.Items.Add(exitItem);


            _trayIcon = new TaskbarIcon
            {
                Icon = _stopIcon ?? SystemIcons.Application,
                ToolTipText = Strings.TrayTooltip,
                ContextMenu = contextMenu
            };

            _trayIcon.TrayLeftMouseDown += (_, _) => ShowSettings();

            // 상태 변경 이벤트 구독
            _mailPollingService.RunningStateChanged += OnPollingStateChanged;
            _mailPollingService.SettingsValidityChanged += OnSettingsValidityChanged;
            _mailPollingService.RefreshEnabledChanged += OnRefreshEnabledChanged;
            _mailPollingService.ErrorOccurred += OnPollingErrorOccurred;
            _mailPollingService.AccountErrorOccurred += OnAccountErrorOccurred;
            _mailPollingService.AccountErrorCleared += OnAccountErrorCleared;
        }

        /// <summary>
        /// 아이콘 로드
        /// </summary>
        private void LoadIcons()
        {
            var basePath = AppContext.BaseDirectory;
            var startIconPath = Path.Combine(basePath, "Assets", "start.ico");
            var stopIconPath = Path.Combine(basePath, "Assets", "stop.ico");

            if (File.Exists(startIconPath))
            {
                _startIcon = new Icon(startIconPath);
            }

            if (File.Exists(stopIconPath))
            {
                _stopIcon = new Icon(stopIconPath);
            }

            var warningIconPath = Path.Combine(basePath, "Assets", "warning.ico");
            if (File.Exists(warningIconPath))
            {
                _warningIcon = new Icon(warningIconPath);
            }
        }

        /// <summary>
        /// 폴링 시작/중지 토글 (IsRefreshEnabled 값도 함께 변경)
        /// </summary>
        private async void TogglePolling()
        {
            try
            {
                var collection = await _settingsService.LoadCollectionAsync();

                // IsRefreshEnabled 값을 토글
                collection.IsRefreshEnabled = !collection.IsRefreshEnabled;

                // 설정 저장
                await _settingsService.SaveCollectionAsync(collection);

                // 폴링 서비스에 적용 (자동으로 시작/중지됨)
                _mailPollingService.ApplySettings(collection);
            }
            catch (Exception)
            {
                // 오류 발생 시 기존 방식으로 fallback
                if (_mailPollingService.IsRunning)
                {
                    _mailPollingService.Stop();
                }
                else
                {
                    _mailPollingService.Start();
                }
            }
        }

        /// <summary>
        /// 폴링 상태 변경 시 UI 업데이트
        /// </summary>
        private void OnPollingStateChanged(bool isRunning)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateTrayUI();
            });
        }

        /// <summary>
        /// 설정 유효성 변경 시 UI 업데이트
        /// </summary>
        private void OnSettingsValidityChanged(bool isValid)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateTrayUI();
            });
        }

        /// <summary>
        /// 새로고침 기능 활성화 변경 시 UI 업데이트
        /// </summary>
        private void OnRefreshEnabledChanged(bool isEnabled)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateTrayUI();
            });
        }

        /// <summary>
        /// 트레이 UI 통합 업데이트 (IsRefreshEnabled 상태 반영)
        /// </summary>
        private void UpdateTrayUI()
        {
            var isRefreshEnabled = _mailPollingService.IsRefreshEnabled;
            var isRunning = _mailPollingService.IsRunning;
            var hasValidSettings = _mailPollingService.HasValidSettings;

            // 메뉴 항목 업데이트
            if (_toggleMenuItem is not null)
            {
                var menuVisibility = isRefreshEnabled ? Visibility.Visible : Visibility.Collapsed;
                _toggleMenuItem.Visibility = menuVisibility;
                _toggleMenuItem.Header = isRunning ? Strings.TrayStopPolling : Strings.TrayStartPolling;
                _toggleMenuItem.IsEnabled = isRefreshEnabled && (isRunning || hasValidSettings);
            }

            if (_toggleMenuSeparator is not null)
            {
                _toggleMenuSeparator.Visibility = isRefreshEnabled ? Visibility.Visible : Visibility.Collapsed;
            }

            // 트레이 아이콘 및 툴팁 업데이트 (IsRefreshEnabled 상태 반영)
            if (_trayIcon is not null)
            {
                // 아이콘 상태 결정:
                // 1. IsRefreshEnabled가 false이거나 폴링 중인 계정이 없으면 stop.ico
                // 2. IsRefreshEnabled가 true이고 오류가 있는 계정이 있으면 warning.ico
                // 3. IsRefreshEnabled가 true이고 모든 계정에 오류가 없으면 start.ico
                Icon icon;
                if (!isRefreshEnabled || !isRunning)
                {
                    icon = _stopIcon ?? SystemIcons.Application;
                }
                else if (_hasAccountError)
                {
                    icon = _warningIcon ?? SystemIcons.Warning;
                }
                else
                {
                    icon = _startIcon ?? SystemIcons.Application;
                }

                _trayIcon.Icon = icon;
                _trayIcon.ToolTipText = GetToolTipText(isRefreshEnabled, isRunning, hasValidSettings, _hasAccountError);
            }
        }

        /// <summary>
        /// 툴팁 텍스트 생성
        /// </summary>
        private static string GetToolTipText(bool isRefreshEnabled, bool isRunning, bool hasValidSettings, bool hasAccountError)
        {
            if (!isRefreshEnabled)
            {
                return Strings.TrayTooltipDisabled;
            }

            if (isRunning)
            {
                return hasAccountError ? Strings.TrayTooltipAccountError : Strings.TrayTooltipRunning;
            }

            return hasValidSettings ? Strings.TrayTooltipStopped : Strings.TrayTooltipNeedSetup;
        }

        /// <summary>
        /// 폴링 오류 발생 시 UI 업데이트
        /// </summary>
        private void OnPollingErrorOccurred()
        {
            Dispatcher.Invoke(() =>
            {
                if (_toggleMenuItem is not null)
                {
                    _toggleMenuItem.Header = Strings.TrayStartPolling;
                    _toggleMenuItem.IsEnabled = false;
                }

                if (_trayIcon is not null)
                {
                    _trayIcon.Icon = _stopIcon ?? SystemIcons.Application;
                    _trayIcon.ToolTipText = Strings.TrayTooltipError;
                }
            });
        }

        /// <summary>
        /// 계정별 오류 발생 시 트레이 아이콘 업데이트
        /// </summary>
        private void OnAccountErrorOccurred(string accountKey, string errorMessage)
        {
            Dispatcher.Invoke(() =>
            {
                _hasAccountError = true;
                UpdateTrayUI();
            });
        }

        /// <summary>
        /// 계정별 오류 해제 시 트레이 아이콘 업데이트
        /// </summary>
        private void OnAccountErrorCleared(string accountKey)
        {
            Dispatcher.Invoke(() =>
            {
                // 모든 계정의 오류 상태를 확인하여 _hasAccountError 갱신
                _hasAccountError = CheckAnyAccountHasError();
                UpdateTrayUI();
            });
        }

        /// <summary>
        /// 폴링 중인 계정 중 오류가 있는지 확인
        /// </summary>
        private bool CheckAnyAccountHasError()
        {
            if (_window?.ViewModel is not ViewModels.SettingsViewModel viewModel)
            {
                return false;
            }

            return viewModel.Accounts.Any(a => a.HasError && a.IsEnabled);
        }

        /// <summary>
        /// 앱 시작 시 저장된 언어 설정 적용 (UI 생성 전 호출)
        /// </summary>
        private static void ApplyStartupLanguage()
        {
            var languageCode = SettingsService.LoadLanguageSync();
            if (!string.IsNullOrEmpty(languageCode))
            {
                try
                {
                    var culture = new CultureInfo(languageCode);
                    Thread.CurrentThread.CurrentUICulture = culture;
                    Thread.CurrentThread.CurrentCulture = culture;
                    Strings.Culture = culture;
                }
                catch
                {
                    // 잘못된 언어 코드 무시
                }
            }
        }

        /// <summary>
        /// 트레이 메뉴 텍스트 언어 갱신
        /// </summary>
        public void RefreshTrayMenuLanguage()
        {
            if (_settingsMenuItem is not null)
            {
                _settingsMenuItem.Header = Strings.TraySettings;
            }

            if (_exitMenuItem is not null)
            {
                _exitMenuItem.Header = Strings.TrayExit;
            }

            UpdateTrayUI();
        }

        /// <summary>
        /// 설정 창 표시
        /// </summary>
        public void ShowSettings()
        {
            if (_window is null || _isExiting)
            {
                return;
            }

            _window.Show();
            _window.WindowState = WindowState.Normal;
            _window.Activate();
        }

        /// <summary>
        /// 서비스 초기화
        /// </summary>
        private async Task InitializeServicesAsync()
        {
            _notificationService.Initialize();
            _notificationService.SaveUidsRequested += OnSaveUidsRequested;

            var collection = await _settingsService.LoadCollectionAsync();
            _mailPollingService.ApplySettings(collection);
        }

        /// <summary>
        /// 알림 클릭 시 UID 저장
        /// </summary>
        private async void OnSaveUidsRequested(string accountKey, IReadOnlyList<string> uids)
        {
            try
            {
                var known = await _mailStateStore.LoadAsync(accountKey, CancellationToken.None);
                foreach (var uid in uids)
                {
                    known.Add(uid);
                }
                await _mailStateStore.SaveAsync(accountKey, known, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UID 저장 실패 [{accountKey}]: {ex.Message}");
            }
        }

        /// <summary>
        /// 앱 종료
        /// </summary>
        private void ExitApp()
        {
            if (_isExiting)
            {
                return;
            }

            _isExiting = true;
            CleanupResources();
            Shutdown();
        }

        /// <summary>
        /// 리소스 정리
        /// </summary>
        private void CleanupResources()
        {
            _mailPollingService.RunningStateChanged -= OnPollingStateChanged;
            _mailPollingService.SettingsValidityChanged -= OnSettingsValidityChanged;
            _mailPollingService.RefreshEnabledChanged -= OnRefreshEnabledChanged;
            _mailPollingService.ErrorOccurred -= OnPollingErrorOccurred;
            _mailPollingService.AccountErrorOccurred -= OnAccountErrorOccurred;
            _mailPollingService.AccountErrorCleared -= OnAccountErrorCleared;
            _mailPollingService.Dispose();
            _notificationService.SaveUidsRequested -= OnSaveUidsRequested;
            _notificationService.Shutdown();
            _trayIcon?.Dispose();
            _trayIcon = null;
            _startIcon?.Dispose();
            _startIcon = null;
            _stopIcon?.Dispose();
            _stopIcon = null;
            _warningIcon?.Dispose();
            _warningIcon = null;

            if (_window != null)
            {
                _window.ForceClose();
                _window = null;
            }

            Instance = null;
        }
    }
}
