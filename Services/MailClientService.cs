using MailKit;
using MailKit.Net.Pop3;
using MailKit.Security;
using MimeKit;
using MailTrayNotifier.Constants;
using MailTrayNotifier.Models;
using MailTrayNotifier.Resources;

namespace MailTrayNotifier.Services
{
    /// <summary>
    /// POP3 메일 조회 서비스
    /// </summary>
    public sealed class MailClientService
    {
        /// <summary>
        /// 메일 서버 접속 테스트
        /// </summary>
        public async Task TestConnectionAsync(MailSettings settings, CancellationToken cancellationToken = default)
        {
            using var client = new Pop3Client
            {
                Timeout = MailConstants.ConnectionTimeoutSeconds * 1000
            };
            var secureOption = settings.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;
            await client.ConnectAsync(settings.Pop3Server, settings.Pop3Port, secureOption, cancellationToken).ConfigureAwait(false);
            await client.AuthenticateAsync(settings.UserId, settings.Password, cancellationToken).ConfigureAwait(false);
            await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 최근 {MailConstants.MaxDaysToCheck}일 이내 메일 UID 및 헤더 정보 조회 (최신순, 최대 {MailConstants.MaxMailsToFetch}개)
        /// </summary>
        public async Task<IReadOnlyList<MailInfo>> GetMailListAsync(MailSettings settings, CancellationToken cancellationToken)
        {
            if (!settings.HasRequiredValues())
            {
                return Array.Empty<MailInfo>();
            }

            using var client = new Pop3Client
            {
                Timeout = MailConstants.ConnectionTimeoutSeconds * 1000
            };
            var secureOption = settings.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;

            try
            {
                await client.ConnectAsync(settings.Pop3Server, settings.Pop3Port, secureOption, cancellationToken).ConfigureAwait(false);
            }
            catch (MailKit.Security.AuthenticationException)
            {
                throw new InvalidOperationException(string.Format(Strings.MailServerAuthFailed, settings.Pop3Server));
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                throw new InvalidOperationException(string.Format(Strings.MailServerConnectionFailed, settings.Pop3Server, ex.Message), ex);
            }
            catch (TimeoutException ex)
            {
                throw new InvalidOperationException(string.Format(Strings.MailServerTimeout, settings.Pop3Server), ex);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidOperationException(string.Format(Strings.MailServerConnectionError, ex.Message), ex);
            }

            try
            {
                await client.AuthenticateAsync(settings.UserId, settings.Password, cancellationToken).ConfigureAwait(false);
            }
            catch (MailKit.Security.AuthenticationException)
            {
                throw new UnauthorizedAccessException(string.Format(Strings.MailAccountAuthFailed, settings.UserId));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidOperationException(string.Format(Strings.MailAuthError, ex.Message), ex);
            }

            if (client.Count == 0)
            {
                await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);
                return Array.Empty<MailInfo>();
            }

            var uids = await client.GetMessageUidsAsync(cancellationToken).ConfigureAwait(false);
            var result = new List<MailInfo>(Math.Min(client.Count, MailConstants.MaxMailsToFetch));
            var cutoffDate = DateTimeOffset.Now.AddDays(-MailConstants.MaxDaysToCheck);
            var fetchCount = 0;
            var consecutiveOldCount = 0;

            // 최신 메일부터 역순으로 조회 (마지막 인덱스 = 최신)
            for (var i = client.Count - 1; i >= 0 && fetchCount < MailConstants.MaxMailsToFetch; i--)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var uid = uids[i];

                // 개별 메일 헤더 조회 실패 시 건너뛰기
                HeaderList headers;
                try
                {
                    headers = await client.GetMessageHeadersAsync(i, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (MailKit.ProtocolException)
                {
                    // POP3 프로토콜 오류는 건너뛰기
                    continue;
                }
                catch (System.IO.IOException)
                {
                    // I/O 오류는 건너뛰기
                    continue;
                }
                catch (TimeoutException)
                {
                    // 타임아웃은 건너뛰기
                    continue;
                }

                // Date 헤더 파싱
                var dateHeader = headers[HeaderId.Date];
                var date = DateTimeOffset.MinValue;
                if (!string.IsNullOrEmpty(dateHeader) && MimeKit.Utils.DateUtils.TryParse(dateHeader, out var parsedDate))
                {
                    date = parsedDate;
                }

                // {MailConstants.MaxDaysToCheck}일 이전 메일이면 건너뛰기 (연속 {MailConstants.ConsecutiveOldMailThreshold}개 이상이면 종료)
                if (date != DateTimeOffset.MinValue && date < cutoffDate)
                {
                    consecutiveOldCount++;
                    if (consecutiveOldCount >= MailConstants.ConsecutiveOldMailThreshold)
                    {
                        break;
                    }
                    continue;
                }

                // 최근 메일 발견 시 연속 카운트 리셋
                consecutiveOldCount = 0;

                // Header 객체에서 디코딩된 값 추출 (MIME 자동 디코딩)
                var fromHeaderObj = headers.FirstOrDefault(h => h.Id == HeaderId.From);
                var subjectHeaderObj = headers.FirstOrDefault(h => h.Id == HeaderId.Subject);

                // Subject: Header.Value는 MIME 디코딩된 값 반환
                var subjectHeader = subjectHeaderObj?.Value ?? string.Empty;

                // From 헤더 파싱
                var senderName = string.Empty;
                var senderEmail = string.Empty;
                if (fromHeaderObj != null)
                {
                    // Header.Value로 디코딩된 문자열 가져온 후 파싱
                    var fromValue = fromHeaderObj.Value;
                    if (!string.IsNullOrEmpty(fromValue) && InternetAddressList.TryParse(fromValue, out var fromAddresses))
                    {
                        var mailbox = fromAddresses.Mailboxes.FirstOrDefault();
                        if (mailbox != null)
                        {
                            senderName = mailbox.Name ?? string.Empty;
                            senderEmail = mailbox.Address ?? string.Empty;
                        }
                    }
                }

                result.Add(new MailInfo
                {
                    Uid = uid,
                    SenderName = senderName,
                    SenderEmail = senderEmail,
                    Subject = subjectHeader ?? string.Empty,
                    Date = date
                });

                fetchCount++;
            }

            await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);
            return result;
        }
    }
}
