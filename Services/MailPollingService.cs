using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using MailTrayNotifier.Constants;
using MailTrayNotifier.Models;

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

            var wasValid = HasValidSettings;
            var wasRefreshEnabled = IsRefreshEnabled;

            _settingsCollection = collection;

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

            // IsRefreshEnabled 값에 따라 폴링 시작/중지
            if (!isRefreshEnabled || !isValid)
            {
                Stop(); // 비활성화 또는 유효한 설정이 없으면 중지
            }
            else
            {
                RestartAllAccountPolling(); // 설정 변경을 반영하기 위해 재시작
            }
        }

        /// <summary>
        /// 폴링 시작
        /// </summary>
        public void Start()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (IsRunning || _settingsCollection is null || _settingsCollection.ValidAccountCount() == 0 || !_settingsCollection.IsRefreshEnabled)
            {
                return;
            }

            StartAllAccountPolling();
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

            StopAllAccountPolling();
            IsRunning = false;
            RunningStateChanged?.Invoke(false);
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
                if (_accountPollingTasks.ContainsKey(accountKey))
                {
                    continue; // 이미 실행 중
                }

                var cts = new CancellationTokenSource();
                _accountPollingTasks.TryAdd(accountKey, cts);
                _ = RunAccountPollingAsync(account, cts);
            }
        }

        /// <summary>
        /// 모든 계정 폴링 중지
        /// </summary>
        private void StopAllAccountPolling()
        {
            foreach (var kvp in _accountPollingTasks)
            {
                kvp.Value.Cancel();
                kvp.Value.Dispose();
                // SemaphoreSlim은 여기서 해제하지 않음
                // 비동기 태스크가 아직 락을 보유 중일 수 있으므로 Dispose()에서 정리
            }

            _accountPollingTasks.Clear();
        }

        /// <summary>
        /// 모든 계정 폴링 재시작
        /// </summary>
        private void RestartAllAccountPolling()
        {
            StopAllAccountPolling();

            if (_settingsCollection is null || _settingsCollection.ValidAccountCount() == 0)
            {
                if (IsRunning)
                {
                    IsRunning = false;
                    RunningStateChanged?.Invoke(false);
                }
                return;
            }

            StartAllAccountPolling();

            // 실제로 폴링 중인 계정이 있는지 확인
            var hasActivePolling = _accountPollingTasks.Count > 0;

            if (hasActivePolling && !IsRunning)
            {
                IsRunning = true;
                RunningStateChanged?.Invoke(true);
            }
            else if (!hasActivePolling && IsRunning)
            {
                IsRunning = false;
                RunningStateChanged?.Invoke(false);
            }
        }

        /// <summary>
        /// 개별 계정 폴링 루프
        /// </summary>
        private async Task RunAccountPollingAsync(MailSettings account, CancellationTokenSource myCts)
        {
            var cancellationToken = myCts.Token;
            var accountKey = account.GetAccountKey();

            try
            {
                // 앱 시작 시 즉시 메일 확인 (일시적 오류 시에도 폴링 루프 진입)
                try
                {
                    await CheckAccountWithLockAsync(account, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (IsTransientNetworkError(ex))
                {
                    System.Diagnostics.Debug.WriteLine($"[{accountKey}] 초기 확인 시 일시적 네트워크 오류, 다음 폴링까지 대기: {ex.Message}");
                }

                // 이후 주기적으로 확인 (계정별 독립 주기)
                using var timer = new PeriodicTimer(TimeSpan.FromMinutes(account.RefreshMinutes));
                while (!cancellationToken.IsCancellationRequested &&
                       await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                {
                    try
                    {
                        await CheckAccountWithLockAsync(account, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex) when (IsTransientNetworkError(ex))
                    {
                        // 일시적 오류는 로그만 남기고 다음 폴링까지 대기
                        System.Diagnostics.Debug.WriteLine($"[{accountKey}] 일시적 네트워크 오류, 다음 폴링까지 대기: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 취소는 정상 종료
            }
            catch (Exception ex)
            {
                // 이미 중지되었거나 새 폴링이 시작된 경우 무시
                // (StopAllAccountPolling 호출 후 발생한 부수 예외)
                if (!_accountPollingTasks.TryGetValue(accountKey, out var currentCts) || currentCts != myCts)
                {
                    return;
                }

                // 영구적 오류는 해당 계정만 중지
                _notificationService.ShowError($"계정 '{account.UserId}@{account.Pop3Server}' 메일 확인 중 오류가 발생했습니다.\n{ex.Message}");

                // 해당 계정 폴링만 중지
                if (_accountPollingTasks.TryRemove(accountKey, out var removedCts))
                {
                    removedCts.Dispose();
                }

                // 계정 관련 리소스 정리
                CleanupAccountResources(accountKey);

                // 모든 계정이 실패하면 전체 중지
                if (_accountPollingTasks.IsEmpty)
                {
                    StopDueToError();
                }
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

                // 메일 확인 성공 시 오류 상태 해제
                AccountErrorCleared?.Invoke(accountKey);

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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[{accountKey}] 메일 확인 오류: {ex.GetType().Name} - {ex.Message}");
                // 메일 확인 실패 시 오류 상태 설정
                AccountErrorOccurred?.Invoke(accountKey, ex.Message);
                throw; // 기존 오류 처리 로직을 유지하기 위해 다시 throw
            }
        }

        /// <summary>
        /// 오류로 인한 전체 중지 (내부용)
        /// </summary>
        private void StopDueToError()
        {
            StopAllAccountPolling();

            if (IsRunning)
            {
                IsRunning = false;
                RunningStateChanged?.Invoke(false);
            }

            // 오류 발생 알림 (메뉴 비활성화용)
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
            Stop();

            // 모든 계정별 락 해제
            foreach (var lockItem in _accountLocks.Values)
            {
                lockItem.Dispose();
            }
            _accountLocks.Clear();
        }
    }
}
