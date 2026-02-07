using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

        // DPAPI 엔트로피 (추가 보안)
        private static readonly byte[] Entropy = "MailTrayNotifier_v1"u8.ToArray();

        /// <summary>
        /// 레거시 단일 계정 설정 로드 (마이그레이션용)
        /// </summary>
        public async Task<MailSettings> LoadAsync()
        {
            if (!File.Exists(SettingsFile))
            {
                return new MailSettings();
            }

            try
            {
                var json = await File.ReadAllTextAsync(SettingsFile).ConfigureAwait(false);
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
            if (!File.Exists(SettingsFile))
            {
                return new MailSettingsCollection();
            }

            try
            {
                var json = await File.ReadAllTextAsync(SettingsFile).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return new MailSettingsCollection();
                }

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

                    // 자동으로 새 형식으로 저장
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

            var json = JsonSerializer.Serialize(settingsToSave, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(SettingsFile, json).ConfigureAwait(false);
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

            var json = JsonSerializer.Serialize(collectionToSave, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(SettingsFile, json).ConfigureAwait(false);
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
        /// 설정 파일 삭제
        /// </summary>
        public void Clear()
        {
            if (File.Exists(SettingsFile))
            {
                File.Delete(SettingsFile);
            }
        }
    }
}
