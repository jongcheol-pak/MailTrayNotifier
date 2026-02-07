using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using MailTrayNotifier.Models;

namespace MailTrayNotifier.Services
{
    /// <summary>
    /// 메일 UID 상태 저장소 (계정별 개별 파일 관리)
    /// </summary>
    public sealed class MailStateStore
    {
        private const int MaxUidsPerAccount = 500;

        private static readonly string StateFolder = Path.Combine(AppContext.BaseDirectory, "mail");
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        // 파일 동시 접근 방지 (계정별 락)
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

        // 메모리 캐시 (계정별 캐시)
        private readonly ConcurrentDictionary<string, AccountState> _cache = new();

        /// <summary>
        /// 계정별 상태 캐시 클래스
        /// </summary>
        private sealed class AccountState
        {
            public HashSet<string> Uids { get; set; } = new();
            public bool IsDirty { get; set; }
        }

        /// <summary>
        /// 생성자 - mail 폴더가 없으면 생성
        /// </summary>
        public MailStateStore()
        {
            if (!Directory.Exists(StateFolder))
            {
                Directory.CreateDirectory(StateFolder);
            }
        }

        /// <summary>
        /// 지정된 계정의 UID 목록 로드 (캐시 우선)
        /// </summary>
        public async Task<HashSet<string>> LoadAsync(string accountKey, CancellationToken cancellationToken)
        {
            var accountLock = GetAccountLock(accountKey);
            await accountLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await LoadFromCacheOrFileAsync(accountKey, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                accountLock.Release();
            }
        }

        /// <summary>
        /// 캐시 또는 파일에서 UID 로드 (락 내부에서 호출, 락 재진입 없음)
        /// </summary>
        private async Task<HashSet<string>> LoadFromCacheOrFileAsync(string accountKey, CancellationToken cancellationToken)
        {
            // 캐시에서 먼저 확인
            if (_cache.TryGetValue(accountKey, out var accountState))
            {
                return new HashSet<string>(accountState.Uids);
            }

            // 파일에서 로드
            var filePath = GetAccountFilePath(accountKey);
            var uids = new HashSet<string>();

            if (File.Exists(filePath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var uidList = JsonSerializer.Deserialize<List<string>>(json);
                        if (uidList != null)
                        {
                            uids = new HashSet<string>(uidList);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    // 파일 읽기 실패 시 빈 목록 반환
                }
            }

            // 캐시에 저장
            _cache[accountKey] = new AccountState { Uids = uids, IsDirty = false };
            return new HashSet<string>(uids);
        }

        /// <summary>
        /// 지정된 계정의 UID 목록 저장 (캐시에 저장 후 파일에 쓰기)
        /// </summary>
        public async Task SaveAsync(string accountKey, HashSet<string> uids, CancellationToken cancellationToken)
        {
            var accountLock = GetAccountLock(accountKey);
            await accountLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // 기존 캐시 가져오기 (락 재진입 방지를 위해 내부 메서드 사용)
                if (!_cache.TryGetValue(accountKey, out var accountState))
                {
                    var existing = await LoadFromCacheOrFileAsync(accountKey, cancellationToken).ConfigureAwait(false);
                    accountState = _cache[accountKey];
                }

                // 새 UID만 추가
                var originalCount = accountState.Uids.Count;
                accountState.Uids.UnionWith(uids);

                // 변경사항이 있는 경우 처리
                if (accountState.Uids.Count != originalCount)
                {
                    // UID 개수 제한 (오래된 것부터 제거)
                    if (accountState.Uids.Count > MaxUidsPerAccount)
                    {
                        var sortedUids = accountState.Uids.OrderBy(uid => uid).ToList();
                        var removeCount = sortedUids.Count - MaxUidsPerAccount;
                        for (int i = 0; i < removeCount; i++)
                        {
                            accountState.Uids.Remove(sortedUids[i]);
                        }
                    }

                    accountState.IsDirty = true;
                }

                // 변경된 경우에만 파일에 쓰기
                if (accountState.IsDirty)
                {
                    await FlushAccountToDiskAsync(accountKey, accountState, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                accountLock.Release();
            }
        }

        /// <summary>
        /// 계정별 락 객체 가져오기 (없으면 생성)
        /// </summary>
        private SemaphoreSlim GetAccountLock(string accountKey)
        {
            return _locks.GetOrAdd(accountKey, _ => new SemaphoreSlim(1, 1));
        }

        /// <summary>
        /// 계정별 파일 경로 생성 (안전한 파일명 변환)
        /// </summary>
        private string GetAccountFilePath(string accountKey)
        {
            // 파일명에 사용할 수 없는 문자들을 안전한 문자로 변환
            var safeAccountKey = string.Join("_", accountKey.Split(Path.GetInvalidFileNameChars()));
            return Path.Combine(StateFolder, $"mail_state_{safeAccountKey}.json");
        }

        /// <summary>
        /// 계정 상태를 파일에 저장
        /// </summary>
        private async Task FlushAccountToDiskAsync(string accountKey, AccountState accountState, CancellationToken cancellationToken)
        {
            var filePath = GetAccountFilePath(accountKey);
            var uidList = accountState.Uids.OrderBy(uid => uid).ToList(); // 정렬된 목록으로 저장
            var json = JsonSerializer.Serialize(uidList, JsonOptions);
            await File.WriteAllTextAsync(filePath, json, cancellationToken).ConfigureAwait(false);
            accountState.IsDirty = false;
        }

        /// <summary>
        /// 모든 상태 초기화
        /// </summary>
        public void Clear()
        {
            // 모든 캐시 제거
            _cache.Clear();

            // mail 폴더의 모든 mail_state_*.json 파일 삭제
            if (Directory.Exists(StateFolder))
            {
                var files = Directory.GetFiles(StateFolder, "mail_state_*.json");
                foreach (var file in files)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                        // 파일 삭제 실패 시 무시
                    }
                }
            }
        }

        /// <summary>
        /// 특정 계정의 상태 삭제
        /// </summary>
        public void ClearAccount(string accountKey)
        {
            var accountLock = GetAccountLock(accountKey);
            accountLock.Wait();
            try
            {
                // 캐시에서 제거
                _cache.TryRemove(accountKey, out _);

                // 파일 삭제
                var filePath = GetAccountFilePath(accountKey);
                if (File.Exists(filePath))
                {
                    try
                    {
                        File.Delete(filePath);
                    }
                    catch
                    {
                        // 파일 삭제 실패 시 무시
                    }
                }
            }
            finally
            {
                accountLock.Release();
            }
        }
    }
}
