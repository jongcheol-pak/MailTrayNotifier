using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Text;
using MailTrayNotifier.Resources;
using MailTrayNotifier.Services;
using MailTrayNotifier.ViewModels;
using MailTrayNotifier.WinUI.Dialogs;
using MailTrayNotifier.WinUI.Theming;
using MailTrayNotifier.WinUI.Tray;
using Microsoft.UI.Dispatching;
using Microsoft.Win32;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using WinUIEx;
// WinUIEx에도 TrayIcon이 있어 모호 → 직접 구현한 트레이로 고정
using TrayIcon = MailTrayNotifier.WinUI.Tray.TrayIcon;

namespace MailTrayNotifier.WinUI
{
    /// <summary>
    /// WinUI 3 앱 진입점. 트레이 상주 + 메일 폴링 + 복구 이벤트 재시작.
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// 복구 이벤트(절전 복귀/잠금 해제/네트워크 연결) 후 폴링 재시작까지의 지연(초).
        /// </summary>
        private const int PollingRestartDelaySeconds = 60;

        public static App? Instance { get; private set; }

        /// <summary>시스템 기본 문화 (앱 시작 시 캡처)</summary>
        public static CultureInfo SystemDefaultCulture { get; } = CultureInfo.CurrentUICulture;

        /// <summary>메인 창 HWND (다이얼로그/파일 선택기 부모용). 창 없으면 0.</summary>
        public nint MainWindowHandle => _window is null ? nint.Zero : WinRT.Interop.WindowNative.GetWindowHandle(_window);

        /// <summary>메인 창 루트 콘텐츠 (테마 적용용). 창 없으면 null.</summary>
        public Microsoft.UI.Xaml.FrameworkElement? MainWindowContent => _window?.Content as Microsoft.UI.Xaml.FrameworkElement;

        /// <summary>메인 창 AppWindow (타이틀바 색상 적용용). 창 없으면 null.</summary>
        public Microsoft.UI.Windowing.AppWindow? MainAppWindow => _window?.AppWindow;

        private string _startupTheme = string.Empty;

        private readonly SettingsService _settingsService = new();
        private readonly NotificationService _notificationService = new();
        private readonly MailClientService _mailClientService = new();
        private readonly MailStateStore _mailStateStore = new();
        private readonly UpdateCheckService _updateCheckService = new();
        private readonly MailPollingService _mailPollingService;

        private DispatcherQueue? _dispatcherQueue;
        private TrayIcon? _trayIcon;
        private MainWindow? _window;
        private bool _isExiting;
        private bool _hasError;

        private CancellationTokenSource? _updateCheckCts;
        private readonly object _resumeLock = new();
        private CancellationTokenSource? _resumeCts;

        // 트레이 메뉴 항목 ID
        private const int MenuToggleId = 0;
        private const int MenuToggleSeparatorId = 1;
        private const int MenuSettingsId = 2;
        private const int MenuExitId = 3;

        public SettingsService SettingsService => _settingsService;
        public MailPollingService MailPollingService => _mailPollingService;
        public MailClientService MailClientService => _mailClientService;
        public MailStateStore MailStateStore => _mailStateStore;
        internal UpdateCheckService UpdateCheckService => _updateCheckService;

        public App()
        {
            Instance = this;
            _mailPollingService = new MailPollingService(_mailClientService, _mailStateStore, _notificationService);
            InitializeComponent();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            // 한국어 레거시 인코딩 지원 (EUC-KR, ISO-2022-KR 등)
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // 저장된 언어/테마 적용 (테마는 창 생성 후 적용)
            ApplyStartupSettings();

            // 최초 실행 시 자동 실행 기본 등록 (이후 사용자가 끈 상태는 존중)
            SettingsViewModel.EnsureFirstRunAutoStartRegistration();

            // 단일 인스턴스는 Program.Main의 AppInstance로 보장 (중복 실행/알림 클릭은 리디렉션됨)
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

            // 미처리 예외 핸들러
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            UnhandledException += OnXamlUnhandledException;

            // 트레이 'Exit' 외 종료(작업 관리자/시스템 종료/로그오프) 대비 최소 정리
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

            // 복구 이벤트 감지 (절전 복귀/잠금 해제/네트워크 복구)
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;
            NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;

            InitializeTray();

            // 창은 생성만 하고 Activate 하지 않아 트레이 상주 (숨김 시작)
            _window = new MainWindow();

            // 창 루트가 생긴 뒤 테마 적용
            ThemeHelper.Apply(_startupTheme);

            // 콜드 스타트가 알림 클릭으로 시작된 경우, 서비스 초기화 후 알림 활성화 처리
            var activatedArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
            _ = InitializeServicesAsync().ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    Debug.WriteLine($"서비스 초기화 실패: {t.Exception?.GetBaseException().Message}");
                }

                // 초기화 실패 여부와 무관하게 알림 활성화는 가능한 처리(메일/업데이트 URL 열기 등)를 수행한다.
                if (IsNotificationActivation(activatedArgs.Kind))
                {
                    DispatchNotificationActivation(activatedArgs.Data);
                }
            }, TaskScheduler.Default);

            // 앱 시작 10분 후 업데이트 확인 (1회)
            ScheduleUpdateCheck();
        }

        /// <summary>
        /// 트레이 아이콘 초기화
        /// </summary>
        private void InitializeTray()
        {
            _trayIcon = new TrayIcon();

            // 초기 상태: 토글/구분선 숨김 (유효 설정이 있을 때만 표시)
            _trayIcon.AddMenuItem(new TrayMenuItem { Id = MenuToggleId, Text = Strings.TrayStartPolling, IsEnabled = false, IsVisible = false });
            _trayIcon.AddMenuItem(new TrayMenuItem { Id = MenuToggleSeparatorId, IsSeparator = true, IsVisible = false });
            _trayIcon.AddMenuItem(new TrayMenuItem { Id = MenuSettingsId, Text = Strings.TraySettings });
            _trayIcon.AddMenuItem(new TrayMenuItem { Id = MenuExitId, Text = Strings.TrayExit });

            _trayIcon.LeftClicked += ShowSettings;
            _trayIcon.MenuItemClicked += OnTrayMenuClicked;

            _trayIcon.Create(Strings.TrayTooltip);
            _trayIcon.SetIcon(GetIconPath("stop.ico"));

            // 폴링 상태 이벤트 구독
            _mailPollingService.RunningStateChanged += OnPollingStateChanged;
            _mailPollingService.SettingsValidityChanged += OnSettingsValidityChanged;
            _mailPollingService.RefreshEnabledChanged += OnRefreshEnabledChanged;
            _mailPollingService.ErrorOccurred += OnPollingErrorOccurred;
            _mailPollingService.AccountErrorOccurred += OnAccountErrorOccurred;
            _mailPollingService.AccountErrorCleared += OnAccountErrorCleared;
        }

        private static string GetIconPath(string fileName)
            => Path.Combine(AppContext.BaseDirectory, "Assets", fileName);

        /// <summary>
        /// 앱 시작 시 저장된 언어/테마 적용 (UI 생성 전 1회 파일 읽기)
        /// </summary>
        private void ApplyStartupSettings()
        {
            var (languageCode, themeCode) = SettingsService.LoadStartupSettingsSync();

            if (!string.IsNullOrEmpty(languageCode))
            {
                SettingsViewModel.ApplyLanguage(languageCode);
            }

            _startupTheme = themeCode;
        }

        private void OnTrayMenuClicked(int id)
        {
            switch (id)
            {
                case MenuToggleId:
                    TogglePolling();
                    break;
                case MenuSettingsId:
                    ShowSettings();
                    break;
                case MenuExitId:
                    ExitApp();
                    break;
            }
        }

        /// <summary>
        /// 폴링 시작/중지 토글 (IsRefreshEnabled 값도 함께 변경)
        /// </summary>
        private async void TogglePolling()
        {
            // 트레이 메뉴의 시작/중지 표시는 IsRunning 기준이므로, 표시된 의도에 맞춰 명시적으로 설정한다.
            // (전 계정 영구 오류로 중지된 경우 IsRefreshEnabled가 true로 남아 있어, 단순 반전 시
            //  첫 "시작" 클릭이 플래그를 꺼버려 폴링이 시작되지 않던 문제 방지)
            var shouldStart = !_mailPollingService.IsRunning;

            try
            {
                var collection = await _settingsService.LoadCollectionAsync();
                collection.IsRefreshEnabled = shouldStart;
                await _settingsService.SaveCollectionAsync(collection);
                _mailPollingService.ApplySettings(collection);
            }
            catch (Exception)
            {
                if (shouldStart)
                {
                    _mailPollingService.Start();
                }
                else
                {
                    _mailPollingService.Stop();
                }
            }
        }

        #region 트레이 UI 업데이트

        private void OnPollingStateChanged(bool isRunning) => _dispatcherQueue?.TryEnqueue(() =>
        {
            // 재시작 시 이전 오류 표시 초기화
            if (isRunning)
            {
                _hasError = false;
            }
            UpdateTrayUI();
        });

        private void OnSettingsValidityChanged(bool isValid) => _dispatcherQueue?.TryEnqueue(UpdateTrayUI);

        private void OnRefreshEnabledChanged(bool isEnabled) => _dispatcherQueue?.TryEnqueue(UpdateTrayUI);

        private void OnPollingErrorOccurred() => _dispatcherQueue?.TryEnqueue(() =>
        {
            _hasError = true;
            UpdateTrayUI();
        });

        private void OnAccountErrorOccurred(string accountKey, string errorMessage) => _dispatcherQueue?.TryEnqueue(() =>
        {
            _hasError = true;
            UpdateTrayUI();
        });

        private void OnAccountErrorCleared(string accountKey) => _dispatcherQueue?.TryEnqueue(() =>
        {
            _hasError = CheckAnyAccountHasError();
            UpdateTrayUI();
        });

        /// <summary>
        /// 폴링 중인 계정 중 오류가 있는지 확인.
        /// 설정 창을 한 번도 열지 않아 ViewModel.Accounts가 비어 있어도 정확하도록
        /// 폴링 서비스의 권위 오류 상태로 판정한다.
        /// </summary>
        private bool CheckAnyAccountHasError() => _mailPollingService.HasAnyAccountError;

        /// <summary>
        /// 트레이 UI 통합 업데이트 (아이콘/툴팁/토글 메뉴)
        /// </summary>
        private void UpdateTrayUI()
        {
            if (_trayIcon is null)
            {
                return;
            }

            var isRefreshEnabled = _mailPollingService.IsRefreshEnabled;
            var isRunning = _mailPollingService.IsRunning;
            var hasValidSettings = _mailPollingService.HasValidSettings;

            // 토글 메뉴 + 구분선 (유효 설정이 있을 때만 표시)
            _trayIcon.UpdateMenuItem(MenuToggleId,
                text: isRunning ? Strings.TrayStopPolling : Strings.TrayStartPolling,
                isEnabled: hasValidSettings, isVisible: hasValidSettings);
            _trayIcon.UpdateMenuItem(MenuToggleSeparatorId, isVisible: hasValidSettings);

            // 아이콘 상태: 비활성/미실행=stop, 오류=warning, 정상=start
            string iconFile;
            if (!isRefreshEnabled || !isRunning)
            {
                iconFile = "stop.ico";
            }
            else if (_hasError)
            {
                iconFile = "warning.ico";
            }
            else
            {
                iconFile = "start.ico";
            }

            _trayIcon.SetIcon(GetIconPath(iconFile));
            _trayIcon.SetToolTip(GetToolTipText(isRefreshEnabled, isRunning, hasValidSettings, _hasError));
        }

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

        #endregion

        #region 복구 이벤트 (절전/잠금/네트워크)

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
            {
                SchedulePollingRestart(PollingRestartDelaySeconds);
            }
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionUnlock)
            {
                SchedulePollingRestart(PollingRestartDelaySeconds);
            }
        }

        private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
        {
            if (e.IsAvailable)
            {
                SchedulePollingRestart(PollingRestartDelaySeconds);
            }
        }

        /// <summary>
        /// 복구 이벤트 발생 시 폴링 재시작 예약 (지연 재시작).
        /// 이미 예약이 진행 중이면 신규 이벤트는 무시한다 — 디바운스 시간이 재시작 지연보다
        /// 짧아 반복 복구 이벤트가 예약을 계속 취소·재생성하며 재시작을 무한히 미루던 문제를 방지한다.
        /// </summary>
        private void SchedulePollingRestart(int delaySeconds)
        {
            CancellationTokenSource cts;
            CancellationToken ct;

            lock (_resumeLock)
            {
                // 재시작 예약이 진행 중이면 신규 이벤트 무시
                if (_resumeCts is not null)
                {
                    return;
                }

                _resumeCts = new CancellationTokenSource();
                cts = _resumeCts;
                ct = cts.Token;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
                    _mailPollingService?.RestartAfterResume();
                }
                catch (OperationCanceledException) { }
                catch (ObjectDisposedException) { }
                catch (Exception ex)
                {
                    Debug.WriteLine($"복구 후 폴링 재시작 실패: {ex.Message}");
                }
                finally
                {
                    // 예약 완료 → 다음 복구 이벤트가 새 예약을 만들 수 있도록 정리
                    lock (_resumeLock)
                    {
                        if (ReferenceEquals(_resumeCts, cts))
                        {
                            _resumeCts = null;
                        }
                    }
                    cts.Dispose();
                }
            }, ct);
        }

        #endregion

        #region 미처리 예외

        private void OnUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                Debug.WriteLine($"미처리 예외: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void OnXamlUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            Debug.WriteLine($"XAML 미처리 예외: {e.Exception.GetType().Name}: {e.Message}");
            e.Handled = true;
        }

        /// <summary>
        /// 트레이 'Exit' 외 종료 경로(작업 관리자/시스템 종료/로그오프) 대비 최소 정리.
        /// ProcessExit는 시간 제한이 있고 UI 스레드가 아니므로, UI 의존 작업은 제외하고
        /// 트레이 아이콘 제거(Win32, 스레드 무관)와 알림 정리만 수행한다.
        /// 정상 종료(ExitApp)에서는 CleanupResources가 ProcessExit 구독을 해제하여 중복 정리를 막는다.
        /// </summary>
        private void OnProcessExit(object? sender, EventArgs e)
        {
            if (_isExiting)
            {
                return;
            }
            try { _trayIcon?.Dispose(); } catch { }
            try { _notificationService.Shutdown(); } catch { }
        }

        #endregion

        #region 업데이트 확인

        /// <summary>
        /// 앱 시작 10분 후 업데이트 확인 예약 (1회)
        /// </summary>
        private void ScheduleUpdateCheck()
        {
            _updateCheckCts = new CancellationTokenSource();
            var ct = _updateCheckCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(10), ct);
                    await CheckAndNotifyUpdateAsync(ct);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Debug.WriteLine($"업데이트 확인 실패: {ex.Message}");
                }
            }, ct);
        }

        private async Task CheckAndNotifyUpdateAsync(CancellationToken cancellationToken)
        {
            var release = await _updateCheckService.GetLatestReleaseAsync(cancellationToken);
            if (release is null)
            {
                return;
            }

            var currentVersionString = _window?.ViewModel.AppVersion ?? "0.0.0";
            if (!Version.TryParse(currentVersionString, out var currentVersion))
            {
                return;
            }

            if (release.Version > currentVersion)
            {
                var latestVersionText = $"{release.Version.Major}.{release.Version.Minor}.{release.Version.Build}";
                _notificationService.ShowUpdateAvailable(latestVersionText, currentVersionString, release.Url);
            }
        }

        #endregion

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
            _window.Activate();

            // 설정 창 열 때마다 업데이트 확인 (실패해도 무시)
            _ = _window.ViewModel.CheckForUpdateAsync().ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    Debug.WriteLine($"업데이트 확인 실패: {t.Exception?.GetBaseException().Message}");
                }
            }, TaskScheduler.Default);
        }

        /// <summary>
        /// 다른 인스턴스(일반 재실행/알림 클릭)가 리디렉션한 활성화를 처리한다.
        /// 일반 재실행은 기존 메인 창을 표시하고, 알림 활성화는 NotificationInvoked가 처리한다.
        /// </summary>
        public void OnRedirectedActivation(AppActivationArguments args)
        {
            if (args.Kind == ExtendedActivationKind.Launch)
            {
                // 일반 중복 실행: 기존 인스턴스의 메인 창 표시
                _dispatcherQueue?.TryEnqueue(ShowSettings);
            }
            else if (IsNotificationActivation(args.Kind))
            {
                // 알림 클릭/버튼: UID 저장 + 메일/업데이트 URL 열기
                DispatchNotificationActivation(args.Data);
            }
        }

        /// <summary>알림 활성화 종류 여부 (AppNotification/레거시 토스트 공통)</summary>
        private static bool IsNotificationActivation(ExtendedActivationKind kind)
            => kind is ExtendedActivationKind.AppNotification or ExtendedActivationKind.ToastNotification;

        /// <summary>
        /// 알림 활성화 데이터를 UI 스레드로 디스패치한다 (AppNotification/레거시 토스트 공통).
        /// 콜드 스타트(OnLaunched)와 리디렉션(OnRedirectedActivation) 두 경로가 공유한다.
        /// </summary>
        private void DispatchNotificationActivation(object? data)
        {
            if (data is AppNotificationActivatedEventArgs notifArgs)
            {
                _dispatcherQueue?.TryEnqueue(() => _notificationService.HandleActivation(notifArgs));
            }
            else if (data is Windows.ApplicationModel.Activation.IToastNotificationActivatedEventArgs toastArgs)
            {
                // 레거시 토스트 활성화: Argument(query string) 파싱
                _dispatcherQueue?.TryEnqueue(() => _notificationService.HandleActivation(toastArgs.Argument));
            }
        }

        /// <summary>
        /// 서비스 초기화
        /// </summary>
        private async Task InitializeServicesAsync()
        {
            try
            {
                _notificationService.Initialize();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"알림 초기화 실패: {ex.Message}");
            }
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
            Exit();
        }

        /// <summary>
        /// 리소스 정리
        /// </summary>
        private void CleanupResources()
        {
            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;

            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;

            // _resumeCts는 SchedulePollingRestart가 _resumeLock 안에서 교체/정리하므로 동일 락으로 보호
            lock (_resumeLock)
            {
                _resumeCts?.Cancel();
                _resumeCts?.Dispose();
                _resumeCts = null;
            }

            _updateCheckCts?.Cancel();
            _updateCheckCts?.Dispose();
            _updateCheckCts = null;

            _mailPollingService.RunningStateChanged -= OnPollingStateChanged;
            _mailPollingService.SettingsValidityChanged -= OnSettingsValidityChanged;
            _mailPollingService.RefreshEnabledChanged -= OnRefreshEnabledChanged;
            _mailPollingService.ErrorOccurred -= OnPollingErrorOccurred;
            _mailPollingService.AccountErrorOccurred -= OnAccountErrorOccurred;
            _mailPollingService.AccountErrorCleared -= OnAccountErrorCleared;

            _notificationService.SaveUidsRequested -= OnSaveUidsRequested;
            _mailPollingService.Dispose();
            _notificationService.Shutdown();
            _trayIcon?.Dispose();
            _trayIcon = null;
            _window?.ForceClose();
            _window = null;

            // UpdateCheckService는 창 종료 후 해제 (ViewModel이 참조 중일 수 있으므로)
            _updateCheckService.Dispose();

            Instance = null;
        }
    }
}
