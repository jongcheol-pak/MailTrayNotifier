using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MailTrayNotifier.Models;

namespace MailTrayNotifier.Services
{
    /// <summary>
    /// 로컬 설정 저장/로드 서비스 (JSON 파일 기반, 다중 계정 지원)
    /// </summary>
    public sealed class SettingsService
    {
        private static readonly string SettingsFolder = AppContext.BaseDirectory;

        private static readonly string SettingsFile = Path.Combine(SettingsFolder, "settings.json");

        private static readonly JsonSerializerOptions s_jsonWriteOptions = new() { WriteIndented = true };

        // 동시 저장 직렬화 (sharing violation 방지)
        private static readonly SemaphoreSlim s_saveLock = new(1, 1);

        // DPAPI 엔트로피 (추가 보안)
        private static readonly byte[] Entropy = "MailTrayNotifier_v1"u8.ToArray();

        /// <summary>
        /// 레거시 단일 계정 설정 로드 (마이그레이션용)
        /// </summary>
        public async Task<MailSettings> LoadAsync()
        {
            try
            {
                var json = await ReadFileGuardedAsync().ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return new MailSettings();
                }

                var settings = JsonSerializer.Deserialize<MailSettings>(json) ?? new MailSettings();

                // 암호화된 비밀번호 복호화
                if (!string.IsNullOrEmpty(settings.Password))
                {
                    settings.Password = DecryptPassword(settings.Password);
                }

                return settings;
            }
            catch (Exception)
            {
                return new MailSettings();
            }
        }

        /// <summary>
        /// 다중 계정 컬렉션 로드 (레거시 자동 마이그레이션 포함)
        /// </summary>
        public async Task<MailSettingsCollection> LoadCollectionAsync()
        {
            string? json;
            try
            {
                json = await ReadFileGuardedAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                return new MailSettingsCollection();
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                return new MailSettingsCollection();
            }

            try
            {
                // 신규 형식 (Accounts 배열) 시도
                try
                {
                    var collection = JsonSerializer.Deserialize<MailSettingsCollection>(json);
                    if (collection != null)
                    {
                        // 비밀번호 복호화
                        foreach (var account in collection.Accounts)
                        {
                            if (!string.IsNullOrEmpty(account.Password))
                            {
                                account.Password = DecryptPassword(account.Password);
                            }
                        }
                        return collection;
                    }
                }
                catch (JsonException)
                {
                    // 레거시 형식이면 마이그레이션
                }

                // 레거시 단일 계정 형식 마이그레이션
                var legacySettings = JsonSerializer.Deserialize<MailSettings>(json);
                if (legacySettings != null)
                {
                    if (!string.IsNullOrEmpty(legacySettings.Password))
                    {
                        legacySettings.Password = DecryptPassword(legacySettings.Password);
                    }

                    var migratedCollection = MigrateLegacySettings(legacySettings);

                    // 저장은 읽기 락 해제 후 수행 (데드락 회피)
                    await SaveCollectionAsync(migratedCollection).ConfigureAwait(false);

                    return migratedCollection;
                }

                return new MailSettingsCollection();
            }
            catch (Exception)
            {
                return new MailSettingsCollection();
            }
        }

        /// <summary>
        /// 레거시 단일 계정 설정을 다중 계정 컬렉션으로 변환
        /// </summary>
        private static MailSettingsCollection MigrateLegacySettings(MailSettings legacy)
        {
            return new MailSettingsCollection
            {
                IsRefreshEnabled = legacy.IsRefreshEnabled,
                Accounts = new List<MailSettings> { legacy }
            };
        }

        /// <summary>
        /// 레거시 단일 계정 저장
        /// </summary>
        public async Task SaveAsync(MailSettings settings)
        {
            // 저장용 복사본 생성 (원본 변경 방지)
            var settingsToSave = new MailSettings
            {
                IsRefreshEnabled = settings.IsRefreshEnabled,
                Pop3Server = settings.Pop3Server,
                Pop3Port = settings.Pop3Port,
                SmtpServer = settings.SmtpServer,
                SmtpPort = settings.SmtpPort,
                UseSsl = settings.UseSsl,
                UserId = settings.UserId,
                Password = !string.IsNullOrEmpty(settings.Password)
                    ? EncryptPassword(settings.Password)
                    : string.Empty,
                RefreshMinutes = settings.RefreshMinutes,
                MailWebUrl = settings.MailWebUrl,
                IsEnabled = settings.IsEnabled,
                AccountName = settings.AccountName
            };

            await WriteJsonAtomicAsync(settingsToSave).ConfigureAwait(false);
        }

        /// <summary>
        /// 다중 계정 컬렉션 저장
        /// </summary>
        public async Task SaveCollectionAsync(MailSettingsCollection collection)
        {
            // 저장용 복사본 생성 (비밀번호 암호화)
            var collectionToSave = new MailSettingsCollection
            {
                IsRefreshEnabled = collection.IsRefreshEnabled,
                Language = collection.Language,
                Theme = collection.Theme,
                Accounts = collection.Accounts.Select(account => new MailSettings
                {
                    Pop3Server = account.Pop3Server,
                    Pop3Port = account.Pop3Port,
                    SmtpServer = account.SmtpServer,
                    SmtpPort = account.SmtpPort,
                    UseSsl = account.UseSsl,
                    UserId = account.UserId,
                    Password = !string.IsNullOrEmpty(account.Password)
                        ? EncryptPassword(account.Password)
                        : string.Empty,
                    RefreshMinutes = account.RefreshMinutes,
                    MailWebUrl = account.MailWebUrl,
                    IsEnabled = account.IsEnabled,
                    AccountName = account.AccountName
                }).ToList()
            };

            await WriteJsonAtomicAsync(collectionToSave).ConfigureAwait(false);
        }

        /// <summary>
        /// 원자적 JSON 파일 쓰기 (임시 파일에 쓴 뒤 Move, 동시 저장 직렬화)
        /// </summary>
        private static async Task WriteJsonAtomicAsync<T>(T obj)
        {
            var json = JsonSerializer.Serialize(obj, s_jsonWriteOptions);
            var tempFile = SettingsFile + ".tmp";

            await s_saveLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await File.WriteAllTextAsync(tempFile, json).ConfigureAwait(false);
                File.Move(tempFile, SettingsFile, overwrite: true);
            }
            finally
            {
                s_saveLock.Release();
            }
        }

        /// <summary>
        /// DPAPI를 사용하여 비밀번호 암호화
        /// </summary>
        private static string EncryptPassword(string password)
        {
            try
            {
                var passwordBytes = Encoding.UTF8.GetBytes(password);
                var encryptedBytes = ProtectedData.Protect(passwordBytes, Entropy, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(encryptedBytes);
            }
            catch (Exception)
            {
                return password;
            }
        }

        /// <summary>
        /// DPAPI를 사용하여 비밀번호 복호화
        /// </summary>
        private static string DecryptPassword(string encryptedPassword)
        {
            try
            {
                var encryptedBytes = Convert.FromBase64String(encryptedPassword);
                var passwordBytes = ProtectedData.Unprotect(encryptedBytes, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(passwordBytes);
            }
            catch (Exception)
            {
                return encryptedPassword;
            }
        }

        /// <summary>
        /// 설정 파일 삭제 (저장 락 경유, tmp 잔여 파일도 함께 정리)
        /// </summary>
        public void Clear()
        {
            s_saveLock.Wait();
            try
            {
                try { File.Delete(SettingsFile); }
                catch (FileNotFoundException) { }

                try { File.Delete(SettingsFile + ".tmp"); }
                catch (FileNotFoundException) { }
            }
            finally
            {
                s_saveLock.Release();
            }
        }

        /// <summary>
        /// 저장된 언어/테마 코드를 동기적으로 로드 (앱 시작 시 UI 생성 전 호출)
        /// </summary>
        public static (string Language, string Theme) LoadStartupSettingsSync()
        {
            try
            {
                var json = ReadFileGuardedSync();
                if (string.IsNullOrWhiteSpace(json))
                {
                    return (string.Empty, string.Empty);
                }

                var node = JsonNode.Parse(json);
                var language = node?["Language"]?.GetValue<string>() ?? string.Empty;
                var theme = node?["Theme"]?.GetValue<string>() ?? string.Empty;
                return (language, theme);
            }
            catch
            {
                return (string.Empty, string.Empty);
            }
        }

        /// <summary>
        /// 설정 파일을 저장 락 경유로 읽기 (동시 Write 중 Move 충돌 방지).
        /// 파일이 없으면 null 반환
        /// </summary>
        private static async Task<string?> ReadFileGuardedAsync()
        {
            await s_saveLock.WaitAsync().ConfigureAwait(false);
            try
            {
                try { return await File.ReadAllTextAsync(SettingsFile).ConfigureAwait(false); }
                catch (FileNotFoundException) { return null; }
            }
            finally
            {
                s_saveLock.Release();
            }
        }

        /// <summary>
        /// 설정 파일을 저장 락 경유로 동기 읽기 (시작 시 UI 생성 전 호출용)
        /// </summary>
        private static string? ReadFileGuardedSync()
        {
            s_saveLock.Wait();
            try
            {
                try { return File.ReadAllText(SettingsFile); }
                catch (FileNotFoundException) { return null; }
            }
            finally
            {
                s_saveLock.Release();
            }
        }
    }
}
