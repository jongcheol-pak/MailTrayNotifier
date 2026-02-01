using Microsoft.Toolkit.Uwp.Notifications;
using LogLibrary;
using MailTrayNotifier.Models;
using System.Diagnostics;

namespace MailTrayNotifier.Services
{
    /// <summary>
    /// Windows 알림 표시 서비스
    /// </summary>
    public sealed class NotificationService
    {
        private const int MaxSubjectLength = 100;
        private const int MaxSenderLength = 50;
        private const string ActionKey = "action";
        private const string ActionMarkAsRead = "markAsRead";
        private const string UidsKey = "uids";
        private const string AccountKeyKey = "accountKey";
        private const string MailWebUrlKey = "mailWebUrl";

        /// <summary>
        /// 알림 클릭 시 UID 저장 요청 이벤트
        /// </summary>
        public event Action<string, IReadOnlyList<string>>? SaveUidsRequested;

        public void Initialize()
        {
            // 비패키지 앱용 AUMID 등록 (알림에 표시될 앱 이름 설정)
            ToastNotificationManagerCompat.OnActivated += OnToastActivated;
        }

        public void Shutdown()
        {
            // 앱 종료 시 알림 기록 정리
            try
            {
                ToastNotificationManagerCompat.OnActivated -= OnToastActivated;
                ToastNotificationManagerCompat.History.Clear();
                ToastNotificationManagerCompat.Uninstall();
            }
            catch
            {
                // 정리 실패 무시
            }
        }

        private void OnToastActivated(ToastNotificationActivatedEventArgsCompat e)
        {
            var args = ToastArguments.Parse(e.Argument);

            if (args.TryGetValue(ActionKey, out var action) && action == ActionMarkAsRead)
            {
                if (args.TryGetValue(AccountKeyKey, out var accountKey) &&
                    args.TryGetValue(UidsKey, out var uidsString))
                {
                    // UID 저장
                    var uids = uidsString.Split(',', StringSplitOptions.RemoveEmptyEntries);
                    SaveUidsRequested?.Invoke(accountKey, uids);

                    // 메일 웹사이트 열기 (URL이 설정된 경우에만)
                    if (args.TryGetValue(MailWebUrlKey, out var mailWebUrl) && 
                        !string.IsNullOrWhiteSpace(mailWebUrl))
                    {
                        OpenMailWebsite(mailWebUrl);
                    }
                }
            }
        }

        /// <summary>
        /// 메일 웹사이트 열기
        /// </summary>
        private static void OpenMailWebsite(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                JsonLogWriter.Log(LogLevel.Error, "브라우저 실행 실패", exception: ex);
            }
        }

        /// <summary>
        /// 새 메일 알림 표시
        /// </summary>
        public void ShowNewMail(IReadOnlyList<MailInfo> newMails, string accountKey, string mailWebUrl)
        {
            if (newMails.Count == 0)
            {
                return;
            }

            try
            {
                // UID를 날짜 오름차순(오래된 것 먼저)으로 정렬하여 저장 순서 보장
                var uidsString = string.Join(",", newMails.OrderBy(m => m.Date).Select(m => m.Uid));

                if (newMails.Count == 1)
                {
                    // 단일 메일: 상세 정보 표시
                    var mail = newMails[0];
                    var builder = new ToastContentBuilder()
                        .AddArgument(ActionKey, ActionMarkAsRead)
                        .AddArgument(AccountKeyKey, accountKey)
                        .AddArgument(UidsKey, uidsString)
                        .AddArgument(MailWebUrlKey, mailWebUrl ?? string.Empty)
                        .SetToastDuration(ToastDuration.Long)
                        .AddText($"보낸 사람: {Truncate(mail.SenderDisplay, MaxSenderLength)}")
                        .AddText($"받은 시간: {mail.Date.LocalDateTime:yyyy년 MM월 dd일 HH시 mm분}")
                        .AddText($"새 메일: {Truncate(mail.Subject, MaxSubjectLength)}");
                    builder.Show();
                }
                else
                {
                    // 여러 메일: 최신 메일 정보 + 총 개수
                    var latest = newMails.MaxBy(m => m.Date) ?? newMails[0];
                    var builder = new ToastContentBuilder()
                        .AddArgument(ActionKey, ActionMarkAsRead)
                        .AddArgument(AccountKeyKey, accountKey)
                        .AddArgument(UidsKey, uidsString)
                                        .AddArgument(MailWebUrlKey, mailWebUrl ?? string.Empty)
                                        .SetToastDuration(ToastDuration.Long)
                                        .AddText($"새 메일 {newMails.Count}건")
                                        .AddText($"보낸 사람: {Truncate(latest.SenderDisplay, MaxSenderLength)}")
                                        .AddText($"최근: {Truncate(latest.Subject, MaxSubjectLength)}");
                                    builder.Show();
                                }
                            }
                            catch (Exception ex)
                            {
                                JsonLogWriter.Log(LogLevel.Error, "알림 표시 실패", exception: ex);
                            }
                        }

                        public void ShowError(string message)
                        {
                            try
                            {
                                new ToastContentBuilder()
                                    .AddText("메일 확인 오류")
                                    .AddText(Truncate(message, 100))
                                    .Show();
                            }
                            catch (Exception ex)
                            {
                                JsonLogWriter.Log(LogLevel.Error, "오류 알림 표시 실패", exception: ex);
                            }
                        }

        private static string Truncate(string? text, int maxLength)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            return text.Length <= maxLength ? text : string.Concat(text.AsSpan(0, maxLength - 3), "...");
        }
    }
}
