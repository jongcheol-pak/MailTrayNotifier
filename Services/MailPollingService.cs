using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using MailTrayNotifier.Constants;
using MailTrayNotifier.Models;
using MailTrayNotifier.Resources;

namespace MailTrayNotifier.Services
{
    /// <summary>
    /// 주기적으로 메일을 확인하는 서비스 (다중 계정 병렬 폴링 지원)
    /// </summary>
    public sealed class MailPollingService : IDisposable
    {
        private readonly MailClientService _mailClientService;
        private readonly MailStateStore _mailStateStore;
        private readonly NotificationService _notificationService;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _accountLocks = new();
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _accountPollingTasks = new();
        private readonly ConcurrentDictionary<string, bool> _accountErrorStates = new();
        private readonly object _stateLock = new();
        private MailSettingsCollection? _settingsCollection;
        private bool _disposed;

        /// <summary>
        /// 폴링 실행 상태
        /// </summary>
        public bool IsRunning { get; private set; }

        /// <summary>
        /// 새로고침 기능 활성화 여부
        /// </summary>
        public bool IsRefreshEnabled => _settingsCollection?.IsRefreshEnabled ?? false;

        /// <summary>
        /// 설정이 유효한지 여부 (시작 가능 여부)
        /// </summary>
        public bool HasValidSettings => _settingsCollection is not null && _settingsCollection.ValidAccountCount() > 0;

        /// <summary>
        /// 폴링 중인 계정 중 현재 오류 상태인 계정이 있는지 여부.
        /// 창 활성화 여부와 무관하게 동작하도록 권위 상태(_accountErrorStates)로 판정한다.
        /// </summary>
        public bool HasAnyAccountError => !_accountErrorStates.IsEmpty;

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

        /// <summary>
        /// 계정별 오류 발생 이벤트 (계정키, 오류메시지)
        /// </summary>
        public event Action<string, string>? AccountErrorOccurred;

        /// <summary>
        /// 계정별 오류 해제 이벤트 (계정키)
        /// </summary>
        public event Action<string>? AccountErrorCleared;

        public MailPollingService(
            MailClientService mailClientService,
            MailStateStore mailStateStore,
            NotificationService notificationService)
        {
            _mailClientService = mailClientService;
            _mailStateStore = mailStateStore;
            _notificationService = notificationService;
        }

        /// <summary>
        /// 레거시 단일 계정 설정 적용 (하위 호환성용)
        /// </summary>
        public void ApplySettings(MailSettings settings)
        {
            var collection = new MailSettingsCollection
            {
                IsRefreshEnabled = settings.IsRefreshEnabled,
                Accounts = new List<MailSettings> { settings }
            };
            ApplySettings(collection);
        }

        /// <summary>
        /// 다중 계정 컬렉션 적용
        /// </summary>
        public void ApplySettings(MailSettingsCollection collection)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            bool fireValidity, fireRefresh, fireRunning;
            bool newIsValid, newIsRefreshEnabled, newIsRunning;

            lock (_stateLock)
            {
                var wasValid = HasValidSettings;
                var wasRefreshEnabled = IsRefreshEnabled;
                var wasRunning = IsRunning;

                _settingsCollection = collection;

                // 삭제/rename된 계정의 계정별 리소스(SemaphoreSlim, 오류 상태) 정리 (누수 방지)
                PruneStaleAccountResources();

                newIsValid = HasValidSettings;
                newIsRefreshEnabled = IsRefreshEnabled;

                // IsRefreshEnabled 값에 따라 폴링 시작/중지 (이벤트 raise는 lock 해제 후)
                bool didRestart = false;
                if (!newIsRefreshEnabled || !newIsValid)
                {
                    StopCoreLocked();
                }
                else
                {
                    RestartAllAccountPollingLocked();
                    didRestart = true;
                }

                newIsRunning = IsRunning;

                fireValidity = wasValid != newIsValid;
                fireRefresh = wasRefreshEnabled != newIsRefreshEnabled;
                // 재시작 분기는 계정별 오류 디듀프 상태(_accountErrorStates)를 초기화하므로, 복구 성공 시
                // AccountErrorCleared가 발동되지 못한다. 실행 중이면 상태 변화 여부와 무관하게
                // RunningStateChanged를 알려, 구독자(App)가 잔여 오류 표시를 초기화하고 이후 폴링 결과로
                // 다시 판정하도록 한다. (RestartAfterResume과 동일 처리)
                fireRunning = (didRestart && newIsRunning) || (wasRunning != newIsRunning);
            }

            if (fireValidity) SettingsValidityChanged?.Invoke(newIsValid);
            if (fireRefresh) RefreshEnabledChanged?.Invoke(newIsRefreshEnabled);
            if (fireRunning) RunningStateChanged?.Invoke(newIsRunning);
        }

        /// <summary>
        /// 폴링 시작
        /// </summary>
        public void Start()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            bool fireRunning = false;

            lock (_stateLock)
            {
                if (IsRunning || _settingsCollection is null || _settingsCollection.ValidAccountCount() == 0 || !_settingsCollection.IsRefreshEnabled)
                {
                    return;
                }

                StartAllAccountPolling();
                IsRunning = true;
                fireRunning = true;
            }

            if (fireRunning) RunningStateChanged?.Invoke(true);
        }

        /// <summary>
        /// 폴링 중지
        /// </summary>
        public void Stop()
        {
            bool fireStopped;
            lock (_stateLock)
            {
                fireStopped = StopCoreLocked();
            }

            if (fireStopped) RunningStateChanged?.Invoke(false);
        }

        /// <summary>
        /// 폴링 중지 (lock 보유 상태에서 호출, 이벤트 raise 없음).
        /// 상태가 실제로 변경된 경우 true 반환
        /// </summary>
        private bool StopCoreLocked()
        {
            if (!IsRunning)
            {
                return false;
            }

            StopAllAccountPolling();
            IsRunning = false;
            return true;
        }

        /// <summary>
        /// 모든 계정 폴링 시작
        /// </summary>
        private void StartAllAccountPolling()
        {
            if (_settingsCollection is null)
            {
                return;
            }

            // 전역 새로고침이 비활성화된 경우 폴링하지 않음
            if (!_settingsCollection.IsRefreshEnabled)
            {
                return;
            }

            foreach (var account in _settingsCollection.Accounts)
            {
                if (!account.HasRequiredValues() || !account.IsEnabled)
                {
                    continue;
                }

                var accountKey = account.GetAccountKey();
                var cts = new CancellationTokenSource();
                if (!_accountPollingTasks.TryAdd(accountKey, cts))
                {
                    // 같은 키가 이미 있으면 새 CTS는 즉시 해제 (leak 방지)
                    cts.Dispose();
                    continue;
                }
                _ = RunAccountPollingAsync(account, cts);
            }
        }

        /// <summary>
        /// 모든 계정 폴링 중지.
        /// CTS Dispose는 각 워커의 finally에서 수행 → Cancel 직후 Dispose race / 이중 Dispose 방지
        /// </summary>
        private void StopAllAccountPolling()
        {
            foreach (var kvp in _accountPollingTasks)
            {
                try { kvp.Value.Cancel(); }
                catch (ObjectDisposedException) { }
            }

            _accountPollingTasks.Clear();

            // 재시작 시 이전 오류 상태가 잘못된 이벤트 디듀프에 영향 주지 않도록 초기화
            _accountErrorStates.Clear();
        }

        /// <summary>
        /// 모든 계정 폴링 재시작 (lock 보유 상태에서 호출, 이벤트 raise 없음)
        /// </summary>
        private void RestartAllAccountPollingLocked()
        {
            StopAllAccountPolling();

            if (_settingsCollection is null || _settingsCollection.ValidAccountCount() == 0)
            {
                if (IsRunning)
                {
                    IsRunning = false;
                }
                return;
            }

            StartAllAccountPolling();

            var hasActivePolling = _accountPollingTasks.Count > 0;
            IsRunning = hasActivePolling;
        }

        /// <summary>
        /// 절전 모드 복귀 시 폴링 재시작
        /// </summary>
        public void RestartAfterResume()
        {
            if (_disposed || _settingsCollection is null)
            {
                return;
            }

            bool fireRunning = false;
            bool newIsRunning = false;

            lock (_stateLock)
            {
                // 새로고침이 활성화되어 있고 유효한 설정이 있는 경우에만 재시작
                if (_settingsCollection.IsRefreshEnabled && _settingsCollection.ValidAccountCount() > 0)
                {
                    System.Diagnostics.Debug.WriteLine("절전 모드 복귀 감지 - 폴링 재시작");
                    var wasRunning = IsRunning;
                    RestartAllAccountPollingLocked();
                    newIsRunning = IsRunning;
                    // 재시작은 계정별 오류 디듀프 상태(_accountErrorStates)를 초기화하므로, 복구 성공 시
                    // AccountErrorCleared가 발동되지 못한다. 또한 일시적 네트워크 오류는 폴링 루프를 멈추지
                    // 않아 IsRunning이 true→true로 유지되어 RunningStateChanged도 발동되지 않는다. 이 때문에
                    // 구독자(App)의 오류 표시가 풀리지 않는다. 따라서 재시작 후 실행 중이면 상태 변화 여부와
                    // 무관하게 RunningStateChanged를 알려, 잔여 오류 표시를 초기화하고 이후 폴링 결과로 다시
                    // 판정하도록 한다.
                    fireRunning = newIsRunning || (wasRunning != newIsRunning);
                }
            }

            if (fireRunning) RunningStateChanged?.Invoke(newIsRunning);
        }

        /// <summary>
        /// 개별 계정 폴링 루프.
        /// 일시적 네트워크 오류는 제한 없이 다음 폴링 주기마다 재시도한다.
        /// 영구 오류는 폴링 주기마다 최대 MailConstants.MaxPermanentErrorAttempts회까지 재시도한 뒤
        /// 해당 계정을 중지한다. 재시도 카운터는 이 워커의 지역 변수이므로, 재시작/계정 토글로
        /// 새 워커가 생성되면 0부터 다시 시작한다(off→on, 트레이 중지→시작, 복구 재시작 시 초기화).
        /// </summary>
        private async Task RunAccountPollingAsync(MailSettings account, CancellationTokenSource myCts)
        {
            var cancellationToken = myCts.Token;
            var accountKey = account.GetAccountKey();

            // 영구 오류 연속 시도 횟수 (성공 시 0으로 초기화)
            var permanentErrorCount = 0;

            // 1회 메일 확인 수행. 계속 폴링하면 true, 영구 오류 한도 초과로 중지되면 false 반환.
            async Task<bool> TryCheckOnceAsync()
            {
                try
                {
                    await CheckAccountWithLockAsync(account, cancellationToken).ConfigureAwait(false);
                    // 성공 시 영구 오류 카운터 초기화
                    permanentErrorCount = 0;
                    return true;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (IsTransientNetworkError(ex))
                {
                    // 일시적 네트워크 오류: 제한 없이 다음 폴링까지 대기 (카운터 증가 안 함)
                    System.Diagnostics.Debug.WriteLine($"[{accountKey}] 일시적 네트워크 오류, 다음 폴링까지 대기: {ex.Message}");
                    return true;
                }
                catch (Exception ex)
                {
                    // 영구 오류: 폴링 주기마다 최대 횟수까지 재시도, 초과 시 계정 중지 (즉시 재시도하지 않음)
                    permanentErrorCount++;
                    System.Diagnostics.Debug.WriteLine($"[{accountKey}] 영구 오류 {permanentErrorCount}/{MailConstants.MaxPermanentErrorAttempts}회: {ex.Message}");

                    if (permanentErrorCount >= MailConstants.MaxPermanentErrorAttempts)
                    {
                        StopAccountDueToPermanentError(account, accountKey, ex, myCts);
                        return false;
                    }

                    // 한도 미만이면 즉시 재시도하지 않고 다음 폴링 주기에 다시 확인
                    return true;
                }
            }

            try
            {
                // 앱 시작 시 즉시 메일 확인 (오류 시에도 폴링 루프 진입하여 다음 주기에 재시도)
                if (!await TryCheckOnceAsync().ConfigureAwait(false))
                {
                    return;
                }

                // 이후 주기적으로 확인 (계정별 독립 주기)
                using var timer = new PeriodicTimer(TimeSpan.FromMinutes(account.RefreshMinutes));
                while (!cancellationToken.IsCancellationRequested &&
                       await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (!await TryCheckOnceAsync().ConfigureAwait(false))
                    {
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 취소는 정상 종료
            }
            catch (Exception ex)
            {
                // 폴링 루프 자체의 예기치 못한 예외: 안전하게 계정 정리
                StopAccountDueToPermanentError(account, accountKey, ex, myCts);
            }
            finally
            {
                // 자신의 CTS는 워커 종료 시점에 Dispose → StopAllAccountPolling의 Cancel/Dispose race 방지
                myCts.Dispose();
            }
        }

        /// <summary>
        /// 영구 오류로 해당 계정 폴링을 중지한다 (오류 알림 + 리소스 정리 + 전체 실패 시 전체 중지).
        /// 이미 중지되었거나 새 폴링이 시작된 워커의 부수 호출은 무시한다.
        /// </summary>
        private void StopAccountDueToPermanentError(MailSettings account, string accountKey, Exception ex, CancellationTokenSource myCts)
        {
            // 이미 중지되었거나 새 폴링이 시작된 경우 무시 (StopAllAccountPolling 호출 후 발생한 부수 호출)
            if (!_accountPollingTasks.TryGetValue(accountKey, out var currentCts) || currentCts != myCts)
            {
                return;
            }

            // 영구적 오류는 해당 계정만 중지
            _notificationService.ShowError(string.Format(Strings.AccountMailCheckError, $"{account.UserId}@{account.Pop3Server}", ex.Message));

            // 딕셔너리에서 제거 (CTS는 워커 finally에서 본인이 Dispose)
            _accountPollingTasks.TryRemove(accountKey, out _);

            // 계정 관련 리소스 정리
            CleanupAccountResources(accountKey);

            // 모든 계정이 실패하면 전체 중지
            if (_accountPollingTasks.IsEmpty)
            {
                StopDueToError();
            }
        }

        /// <summary>
        /// 계정별 메일 확인 (계정별 독립적 락 사용)
        /// </summary>
        private async Task CheckAccountWithLockAsync(MailSettings account, CancellationToken cancellationToken)
        {
            var accountKey = account.GetAccountKey();
            var accountLock = GetAccountLock(accountKey);

            // 계정별 독립 락 사용 (상수화된 대기 시간 사용)
            if (!await accountLock.WaitAsync(TimeSpan.FromSeconds(MailConstants.AccountLockTimeoutSeconds), cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            try
            {
                await CheckAccountAsync(account, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                accountLock.Release();
            }
        }

        /// <summary>
        /// 개별 계정 메일 확인
        /// </summary>
        private async Task CheckAccountAsync(MailSettings account, CancellationToken cancellationToken)
        {
            var accountKey = account.GetAccountKey();

            // 네트워크 상태 확인 (사용 불가 시 다음 폴링까지 대기)
            if (!IsNetworkAvailable())
            {
                System.Diagnostics.Debug.WriteLine($"[{accountKey}] 네트워크 사용 불가, 다음 폴링까지 대기");
                return;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"[{accountKey}] 메일 확인 시작...");
                var mails = await _mailClientService.GetMailListAsync(account, cancellationToken).ConfigureAwait(false);
                System.Diagnostics.Debug.WriteLine($"[{accountKey}] 서버에서 {mails.Count}개 메일 조회됨");

                // 메일 확인 성공 시 오류 상태 해제 (이전에 오류였던 경우만 이벤트 발동)
                if (_accountErrorStates.TryRemove(accountKey, out _))
                {
                    AccountErrorCleared?.Invoke(accountKey);
                }

                if (mails.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[{accountKey}] 메일 없음");
                    return;
                }

                var known = await _mailStateStore.LoadAsync(accountKey, cancellationToken).ConfigureAwait(false);
                System.Diagnostics.Debug.WriteLine($"[{accountKey}] 기존 읽음 처리된 메일: {known.Count}개");

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
                    System.Diagnostics.Debug.WriteLine($"[{accountKey}] 새 메일 {newMails.Count}개 발견! 알림 표시 중...");
                    // 알림 표시 (클릭 시 UID 저장됨, URL이 설정된 경우 웹사이트 열림)
                    var accountName = string.IsNullOrWhiteSpace(account.AccountName)
                        ? $"{account.UserId}@{account.Pop3Server}"
                        : account.AccountName;
                    _notificationService.ShowNewMail(newMails, accountKey, account.MailWebUrl, accountName);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[{accountKey}] 새 메일 없음 (모두 이미 읽음 처리됨)");
                }
            }
            catch (OperationCanceledException)
            {
                // 중지 토글 등 취소가 아래 AccountErrorOccurred로 잘못 발동되지 않도록 먼저 필터링
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[{accountKey}] 메일 확인 오류: {ex.GetType().Name} - {ex.Message}");
                // 이전에 오류가 아니었던 경우만 이벤트 발동 (연속 폴링 실패 시 중복 UI 업데이트 방지)
                if (_accountErrorStates.TryAdd(accountKey, true))
                {
                    AccountErrorOccurred?.Invoke(accountKey, ex.Message);
                }
                throw;
            }
        }

        /// <summary>
        /// 오류로 인한 전체 중지 (내부용).
        /// ErrorOccurred는 상태 변화 여부와 무관하게 항상 raise (UI가 오류 표시를 갱신해야 하므로)
        /// </summary>
        private void StopDueToError()
        {
            bool fireStopped;
            lock (_stateLock)
            {
                fireStopped = StopCoreLocked();
            }

            if (fireStopped) RunningStateChanged?.Invoke(false);
            ErrorOccurred?.Invoke();
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
        /// 계정별 리소스 정리 (메모리 누수 방지)
        /// </summary>
        private void CleanupAccountResources(string accountKey)
        {
            if (_accountLocks.TryRemove(accountKey, out var semaphore))
            {
                semaphore.Dispose();
            }
            _accountErrorStates.TryRemove(accountKey, out _);
        }

        /// <summary>
        /// 현재 설정에 없는 계정의 리소스 제거 (rename/삭제로 인한 leak 방지, lock 보유 상태에서 호출)
        /// </summary>
        private void PruneStaleAccountResources()
        {
            if (_settingsCollection is null)
            {
                return;
            }

            var validKeys = new HashSet<string>(
                _settingsCollection.Accounts.Select(a => a.GetAccountKey()),
                StringComparer.Ordinal);

            foreach (var key in _accountLocks.Keys)
            {
                if (!validKeys.Contains(key))
                {
                    CleanupAccountResources(key);
                }
            }

            foreach (var key in _accountErrorStates.Keys)
            {
                if (!validKeys.Contains(key))
                {
                    _accountErrorStates.TryRemove(key, out _);
                }
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

            // InvalidOperationException이지만 네트워크 관련 메시지인 경우 (MailClientService에서 래핑된 예외)
            if (ex is InvalidOperationException &&
                (ex.Message.Contains("연결", StringComparison.OrdinalIgnoreCase) ||
                 ex.Message.Contains("connect", StringComparison.OrdinalIgnoreCase) ||
                 ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                 ex.Message.Contains("시간이 초과", StringComparison.OrdinalIgnoreCase) ||
                 ex.Message.Contains("네트워크", StringComparison.OrdinalIgnoreCase) ||
                 ex.Message.Contains("network", StringComparison.OrdinalIgnoreCase)))
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

        /// <summary>
        /// 계정별 락 가져오기 (없으면 새로 생성)
        /// </summary>
        private SemaphoreSlim GetAccountLock(string accountKey)
        {
            return _accountLocks.GetOrAdd(accountKey, _ => new SemaphoreSlim(1, 1));
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            bool fireStopped;
            lock (_stateLock)
            {
                fireStopped = StopCoreLocked();
            }

            if (fireStopped) RunningStateChanged?.Invoke(false);

            // 모든 계정별 락 해제
            foreach (var lockItem in _accountLocks.Values)
            {
                lockItem.Dispose();
            }
            _accountLocks.Clear();
        }
    }
}
