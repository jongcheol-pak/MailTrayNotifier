using System.Net.NetworkInformation;
using MailTrayNotifier.Models;

namespace MailTrayNotifier.Services
{
    /// <summary>
    /// 주기적으로 메일을 확인하는 서비스
    /// </summary>
    public sealed class MailPollingService : IDisposable
    {
        private readonly MailClientService _mailClientService;
        private readonly MailStateStore _mailStateStore;
        private readonly NotificationService _notificationService;
        private readonly SemaphoreSlim _checkLock = new(1, 1);
        private CancellationTokenSource? _cts;
        private Task? _pollingTask;
        private MailSettings? _settings;
        private bool _disposed;

        /// <summary>
        /// 폴링 실행 상태
        /// </summary>
        public bool IsRunning { get; private set; }

        /// <summary>
        /// 새로고침 기능 활성화 여부
        /// </summary>
        public bool IsRefreshEnabled => _settings?.IsRefreshEnabled ?? false;

        /// <summary>
        /// 설정이 유효한지 여부 (시작 가능 여부)
        /// </summary>
        public bool HasValidSettings => _settings is not null && _settings.HasRequiredValues();

        /// <summary>
        /// 상태 변경 이벤트
        /// </summary>
        public event Action<bool>? RunningStateChanged;

        /// <summary>
        /// 설정 유효성 변경 이벤트
        /// </summary>
        public event Action<bool>? SettingsValidityChanged;

        /// <summary>
        /// 새로고침 기능 활성화 변경 이벤트
        /// </summary>
        public event Action<bool>? RefreshEnabledChanged;

        /// <summary>
        /// 오류로 인한 중지 이벤트 (메뉴 비활성화용)
        /// </summary>
        public event Action? ErrorOccurred;

        public MailPollingService(
            MailClientService mailClientService,
            MailStateStore mailStateStore,
            NotificationService notificationService)
        {
            _mailClientService = mailClientService;
            _mailStateStore = mailStateStore;
            _notificationService = notificationService;
        }

        public void ApplySettings(MailSettings settings)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var wasValid = HasValidSettings;
            var wasRefreshEnabled = IsRefreshEnabled;

            _settings = settings;

            var isValid = HasValidSettings;
            var isRefreshEnabled = IsRefreshEnabled;

            if (wasValid != isValid)
            {
                SettingsValidityChanged?.Invoke(isValid);
            }

            if (wasRefreshEnabled != isRefreshEnabled)
            {
                RefreshEnabledChanged?.Invoke(isRefreshEnabled);
            }

            // 새로고침이 비활성화되면 중지
            if (!isRefreshEnabled)
            {
                Stop();
                return;
            }

            Restart();
        }

        /// <summary>
        /// 폴링 시작
        /// </summary>
        public void Start()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (IsRunning || _settings is null || !_settings.HasRequiredValues())
            {
                return;
            }

            _cts = new CancellationTokenSource();
            _pollingTask = RunAsync(_cts.Token);
            IsRunning = true;
            RunningStateChanged?.Invoke(true);
        }

        /// <summary>
        /// 폴링 중지
        /// </summary>
        public void Stop()
        {
            if (!IsRunning)
            {
                return;
            }

            if (_cts is not null)
            {
                _cts.Cancel();

                // 폴링 태스크 완료 대기 (최대 5초)
                try
                {
                    _pollingTask?.Wait(TimeSpan.FromSeconds(5));
                }
                catch (AggregateException)
                {
                    // 취소 예외 무시
                }

                _cts.Dispose();
                _cts = null;
                _pollingTask = null;
            }

            IsRunning = false;
            RunningStateChanged?.Invoke(false);
        }

        private void Restart()
        {
            // 이벤트 발생 없이 내부적으로 중지
            if (_cts is not null)
            {
                _cts.Cancel();
                try
                {
                    _pollingTask?.Wait(TimeSpan.FromSeconds(5));
                }
                catch (AggregateException)
                {
                }
                _cts.Dispose();
                _cts = null;
                _pollingTask = null;
            }

            if (_settings is null || !_settings.HasRequiredValues())
            {
                if (IsRunning)
                {
                    IsRunning = false;
                    RunningStateChanged?.Invoke(false);
                }
                return;
            }

            _cts = new CancellationTokenSource();
            _pollingTask = RunAsync(_cts.Token);

            if (!IsRunning)
            {
                IsRunning = true;
                RunningStateChanged?.Invoke(true);
            }
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            if (_settings is null)
            {
                return;
            }

            // 앱 시작 시 즉시 메일 확인
            await CheckOnceWithLockAsync(_settings, cancellationToken).ConfigureAwait(false);

            // 이후 주기적으로 확인
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_settings.RefreshMinutes));
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await CheckOnceWithLockAsync(_settings, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task CheckOnceWithLockAsync(MailSettings settings, CancellationToken cancellationToken)
        {
            // 동시 실행 방지
            if (!await _checkLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            try
            {
                await CheckOnceAsync(settings, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 취소는 정상 종료
            }
            catch (Exception ex) when (IsTransientNetworkError(ex))
            {
                
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"메일 확인 중 오류가 발생했습니다.\n{ex.Message}");

                // 오류 발생 시 자동 중지
                StopDueToError();
            }
            finally
            {
                _checkLock.Release();
            }
        }

        /// <summary>
        /// 오류로 인한 중지 (내부용)
        /// </summary>
        private void StopDueToError()
        {
            if (_cts is not null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
                _pollingTask = null;
            }

            if (IsRunning)
            {
                IsRunning = false;
                RunningStateChanged?.Invoke(false);
            }

            // 오류 발생 알림 (메뉴 비활성화용)
            ErrorOccurred?.Invoke();
        }

        private async Task CheckOnceAsync(MailSettings settings, CancellationToken cancellationToken)
        {
            // 네트워크 상태 확인 (사용 불가 시 다음 폴링까지 대기)
            if (!IsNetworkAvailable())
            {
                return;
            }

            var mails = await _mailClientService.GetMailListAsync(settings, cancellationToken).ConfigureAwait(false);
            if (mails.Count == 0)
            {
                return;
            }

            var accountKey = settings.GetAccountKey();
            var known = await _mailStateStore.LoadAsync(accountKey, cancellationToken).ConfigureAwait(false);

            // 새 메일 필터링 (지연 평가로 불필요한 List 생성 방지)
            List<MailInfo>? newMails = null;
            foreach (var mail in mails)
            {
                if (!known.Contains(mail.Uid))
                {
                    newMails ??= new List<MailInfo>();
                    newMails.Add(mail);
                }
            }

            if (newMails is { Count: > 0 })
            {
                // 알림 표시 (클릭 시 UID 저장됨, URL이 설정된 경우 웹사이트 열림)
                _notificationService.ShowNewMail(newMails, accountKey, settings.MailWebUrl);
            }
        }

        /// <summary>
        /// 네트워크 사용 가능 여부 확인
        /// </summary>
        private static bool IsNetworkAvailable()
        {
            try
            {
                return NetworkInterface.GetIsNetworkAvailable();
            }
            catch
            {
                // 확인 실패 시 사용 가능으로 간주 (실제 연결 시 오류 처리됨)
                return true;
            }
        }

        /// <summary>
        /// 일시적 네트워크 오류 여부 확인 (재시도 가능한 오류)
        /// </summary>
        private static bool IsTransientNetworkError(Exception ex)
        {
            // 소켓/네트워크 관련 예외
            if (ex is System.Net.Sockets.SocketException)
            {
                return true;
            }

            // IOException (네트워크 스트림 오류 포함)
            if (ex is System.IO.IOException ioEx &&
                ioEx.InnerException is System.Net.Sockets.SocketException)
            {
                return true;
            }

            // MailKit 연결 오류
            if (ex.GetType().FullName?.Contains("MailKit") == true &&
                (ex.Message.Contains("connect", StringComparison.OrdinalIgnoreCase) ||
                 ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                 ex.Message.Contains("network", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            // 타임아웃 예외
            if (ex is TimeoutException)
            {
                return true;
            }

            // 내부 예외 확인
            if (ex.InnerException is not null)
            {
                return IsTransientNetworkError(ex.InnerException);
            }

            return false;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Stop();
            _checkLock.Dispose();
        }
    }
}
