using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LogLibrary;
using MailTrayNotifier.Models;

namespace MailTrayNotifier.Services
{
    /// <summary>
    /// 로컬 설정 저장/로드 서비스 (JSON 파일 기반)
    /// </summary>
    public sealed class SettingsService
    {
        private static readonly string SettingsFolder = AppContext.BaseDirectory;

        private static readonly string SettingsFile = Path.Combine(SettingsFolder, "settings.json");

        // DPAPI 엔트로피 (추가 보안)
        private static readonly byte[] Entropy = "MailTrayNotifier_v1"u8.ToArray();

        public Task<MailSettings> LoadAsync()
        {
            if (!File.Exists(SettingsFile))
            {
                return Task.FromResult(new MailSettings());
            }

            try
            {
                var json = File.ReadAllText(SettingsFile);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return Task.FromResult(new MailSettings());
                }

                var settings = JsonSerializer.Deserialize<MailSettings>(json) ?? new MailSettings();

                // 암호화된 비밀번호 복호화
                if (!string.IsNullOrEmpty(settings.Password))
                {
                    settings.Password = DecryptPassword(settings.Password);
                }

                return Task.FromResult(settings);
            }
            catch (Exception ex)
            {
                JsonLogWriter.Log(LogLevel.Error, "설정 파일 로드 실패", exception: ex);
                return Task.FromResult(new MailSettings());
            }
        }

        public Task SaveAsync(MailSettings settings)
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
                MailWebUrl = settings.MailWebUrl
            };

            var json = JsonSerializer.Serialize(settingsToSave, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFile, json);

            return Task.CompletedTask;
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
                    catch (Exception ex)
                    {
                        JsonLogWriter.Log(LogLevel.Error, "비밀번호 암호화 실패", exception: ex);
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
                    catch (Exception ex)
                    {
                JsonLogWriter.Log(LogLevel.Warning, "비밀번호 복호화 실패 (평문 저장된 기존 설정일 수 있음)", exception: ex);
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
