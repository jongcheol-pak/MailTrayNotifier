using System.IO;
using System.Text.Json;
using LogLibrary;
using MailTrayNotifier.Models;

namespace MailTrayNotifier.Services
{
    /// <summary>
    /// 메일 UID 상태 저장소 (메모리 캐싱 + 비동기 I/O)
    /// </summary>
    public sealed class MailStateStore
    {
        private const int MaxUidsPerAccount = 500;

        private static readonly string StateFolder = AppContext.BaseDirectory;
        private static readonly string StateFile = Path.Combine(StateFolder, "mail_state.json");
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        // 파일 동시 접근 방지
        private readonly SemaphoreSlim _lock = new(1, 1);

        // 메모리 캐시 (파일 I/O 최소화)
        private MailStateFile? _cache;
        private bool _isDirty;

        /// <summary>
        /// 지정된 계정의 UID 목록 로드 (캐시 우선)
        /// </summary>
        public async Task<HashSet<string>> LoadAsync(string accountKey, CancellationToken cancellationToken)
        {
            await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);

                if (_cache!.Accounts.TryGetValue(accountKey, out var uidList))
                {
                    return new HashSet<string>(uidList);
                }

                return new HashSet<string>();
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// 지정된 계정의 UID 목록 저장 (캐시에 저장 후 파일에 쓰기)
        /// </summary>
        public async Task SaveAsync(string accountKey, HashSet<string> uids, CancellationToken cancellationToken)
        {
            await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);

                // 기존 목록 가져오기
                if (!_cache!.Accounts.TryGetValue(accountKey, out var existingList))
                {
                    existingList = new List<string>();
                    _cache.Accounts[accountKey] = existingList;
                }

                // HashSet으로 중복 체크 최적화 (O(1) vs O(n))
                var existingSet = new HashSet<string>(existingList);

                // 새 UID만 추가 (순서 보장: 기존 목록 끝에 추가)
                foreach (var uid in uids)
                {
                    if (existingSet.Add(uid))
                    {
                        existingList.Add(uid);
                        _isDirty = true;
                    }
                }

                // UID 개수 제한 (오래된 것부터 제거 - 앞쪽이 오래된 것)
                if (existingList.Count > MaxUidsPerAccount)
                {
                    var removeCount = existingList.Count - MaxUidsPerAccount;
                    existingList.RemoveRange(0, removeCount);
                    _isDirty = true;
                }

                // 변경된 경우에만 파일에 쓰기
                if (_isDirty)
                {
                    await FlushToDiskAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// 캐시가 로드되지 않았으면 파일에서 로드
        /// </summary>
        private async Task EnsureCacheLoadedAsync(CancellationToken cancellationToken)
        {
            if (_cache is not null)
            {
                return;
            }

            if (!File.Exists(StateFile))
            {
                _cache = new MailStateFile();
                return;
            }

            try
            {
                var json = await File.ReadAllTextAsync(StateFile, cancellationToken).ConfigureAwait(false);
                _cache = string.IsNullOrWhiteSpace(json)
                    ? new MailStateFile()
                    : JsonSerializer.Deserialize<MailStateFile>(json) ?? new MailStateFile();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                JsonLogWriter.Log(LogLevel.Error, "메일 상태 파일 로드 실패", exception: ex);
                _cache = new MailStateFile();
            }
        }

        /// <summary>
        /// 캐시를 파일에 쓰기
        /// </summary>
        private async Task FlushToDiskAsync(CancellationToken cancellationToken)
        {
            if (_cache is null)
            {
                return;
            }

            var json = JsonSerializer.Serialize(_cache, JsonOptions);
            await File.WriteAllTextAsync(StateFile, json, cancellationToken).ConfigureAwait(false);
            _isDirty = false;
        }

        /// <summary>
        /// 모든 상태 초기화
        /// </summary>
        public void Clear()
        {
            _lock.Wait();
            try
            {
                _cache = null;
                _isDirty = false;

                if (File.Exists(StateFile))
                {
                    File.Delete(StateFile);
                }
            }
            finally
            {
                _lock.Release();
            }
        }
    }
}
