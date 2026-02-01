using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using LogLibrary;
using MailTrayNotifier.Services;

namespace MailTrayNotifier
{
    /// <summary>
    /// WPF 앱 진입점
    /// </summary>
    public partial class App : Application
    {
        private const string MutexName = "MailTrayNotifier_SingleInstance_Mutex";
        private static Mutex? _mutex;

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
        private Icon? _startIcon;
        private Icon? _stopIcon;

        public App()
        {
            Instance = this;
            _mailPollingService = new MailPollingService(_mailClientService, _mailStateStore, _notificationService);
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            // 한국어 레거시 인코딩 지원 (EUC-KR, ISO-2022-KR 등)
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // 중복 실행 방지
            _mutex = new Mutex(true, MutexName, out var isNewInstance);
            if (!isNewInstance)
            {
                MessageBox.Show("메일 알림이 이미 실행 중입니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
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

            _ = InitializeServicesAsync();
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
                JsonLogWriter.Log(LogLevel.Critical, "Unhandled exception", exception: ex);
            }
        }

        private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            JsonLogWriter.Log(LogLevel.Error, "Dispatcher unhandled exception", exception: e.Exception);
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
                Header = "메일 알림 시작",
                IsEnabled = false,
                Visibility = Visibility.Collapsed
            };
            _toggleMenuItem.Click += (_, _) => TogglePolling();
            contextMenu.Items.Add(_toggleMenuItem);

            _toggleMenuSeparator = new Separator { Visibility = Visibility.Collapsed };
            contextMenu.Items.Add(_toggleMenuSeparator);

            var settingsItem = new MenuItem { Header = "설정" };
            settingsItem.Click += (_, _) => ShowSettings();
            contextMenu.Items.Add(settingsItem);

            var exitItem = new MenuItem { Header = "종료" };
            exitItem.Click += (_, _) => ExitApp();
            contextMenu.Items.Add(exitItem);


            _trayIcon = new TaskbarIcon
            {
                Icon = _stopIcon ?? SystemIcons.Application,
                ToolTipText = "메일 알림",
                ContextMenu = contextMenu
            };

            _trayIcon.TrayLeftMouseDown += (_, _) => ShowSettings();

            // 상태 변경 이벤트 구독
            _mailPollingService.RunningStateChanged += OnPollingStateChanged;
            _mailPollingService.SettingsValidityChanged += OnSettingsValidityChanged;
            _mailPollingService.RefreshEnabledChanged += OnRefreshEnabledChanged;
            _mailPollingService.ErrorOccurred += OnPollingErrorOccurred;
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
        }

        /// <summary>
        /// 폴링 시작/중지 토글
        /// </summary>
        private void TogglePolling()
        {
            if (_mailPollingService.IsRunning)
            {
                _mailPollingService.Stop();
            }
            else
            {
                _mailPollingService.Start();
            }
        }

        /// <summary>
        /// 폴링 상태 변경 시 UI 업데이트
        /// </summary>
        private void OnPollingStateChanged(bool isRunning)
        {
            Dispatcher.Invoke(() =>
            {
                if (_toggleMenuItem is not null)
                {
                    _toggleMenuItem.Header = isRunning ? "메일 알림 중지" : "메일 알림 시작";
                    // 실행 중이 아닐 때는 설정 유효성에 따라 활성화
                    _toggleMenuItem.IsEnabled = isRunning || _mailPollingService.HasValidSettings;
                }


                if (_trayIcon is not null)
                {
                    _trayIcon.Icon = isRunning ? (_startIcon ?? SystemIcons.Application) : (_stopIcon ?? SystemIcons.Application);
                    _trayIcon.ToolTipText = isRunning
                        ? "메일 알림 - 실행 중"
                        : (_mailPollingService.HasValidSettings ? "메일 알림 - 중지됨" : "메일 알림 - 설정 필요");
                }
            });
        }

        /// <summary>
        /// 설정 유효성 변경 시 UI 업데이트
        /// </summary>
        private void OnSettingsValidityChanged(bool isValid)
        {
            Dispatcher.Invoke(() =>
            {
                if (_toggleMenuItem is not null && !_mailPollingService.IsRunning && _mailPollingService.IsRefreshEnabled)
                {
                    _toggleMenuItem.IsEnabled = isValid;
                }

                if (_trayIcon is not null && !_mailPollingService.IsRunning)
                {
                    _trayIcon.ToolTipText = isValid ? "메일 알림 - 중지됨" : "메일 알림 - 설정 필요";
                }
            });
        }

        /// <summary>
        /// 새로고침 기능 활성화 변경 시 UI 업데이트
        /// </summary>
        private void OnRefreshEnabledChanged(bool isEnabled)
        {
            Dispatcher.Invoke(() =>
            {
                var visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;

                if (_toggleMenuItem is not null)
                {
                    _toggleMenuItem.Visibility = visibility;
                    _toggleMenuItem.IsEnabled = isEnabled && _mailPollingService.HasValidSettings;
                }

                if (_toggleMenuSeparator is not null)
                {
                    _toggleMenuSeparator.Visibility = visibility;
                }

                if (_trayIcon is not null && !_mailPollingService.IsRunning)
                {
                    _trayIcon.ToolTipText = isEnabled
                        ? (_mailPollingService.HasValidSettings ? "메일 알림 - 중지됨" : "메일 알림 - 설정 필요")
                        : "메일 알림";
                }
            });
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
                    _toggleMenuItem.Header = "메일 알림 시작";
                    _toggleMenuItem.IsEnabled = false;
                }

                if (_trayIcon is not null)
                {
                    _trayIcon.Icon = _stopIcon ?? SystemIcons.Application;
                    _trayIcon.ToolTipText = "메일 알림 - 오류 발생";
                }
            });
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

            var settings = await _settingsService.LoadAsync();
            _mailPollingService.ApplySettings(settings);
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
                JsonLogWriter.Log(LogLevel.Error, "UID 저장 실패", exception: ex);
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
            _mailPollingService.Dispose();
            _notificationService.Shutdown();
            _trayIcon?.Dispose();
            _trayIcon = null;
            _startIcon?.Dispose();
            _startIcon = null;
            _stopIcon?.Dispose();
            _stopIcon = null;

            if (_window != null)
            {
                _window.ForceClose();
                _window = null;
            }

            Instance = null;
        }
    }
}
