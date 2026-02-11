using Microsoft.Toolkit.Uwp.Notifications;
using MailTrayNotifier.Models;
using MailTrayNotifier.Resources;
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
        private const string ActionGoToMail = "goToMail";
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

            if (!args.TryGetValue(AccountKeyKey, out var accountKey) ||
                !args.TryGetValue(UidsKey, out var uidsString))
            {
                return;
            }

            var uids = uidsString.Split(',', StringSplitOptions.RemoveEmptyEntries);

            // UID 저장 (모든 경우에 저장)
            SaveUidsRequested?.Invoke(accountKey, uids);

            // 액션에 따라 추가 동작 수행
            if (args.TryGetValue(ActionKey, out var action) && action == ActionGoToMail)
            {
                // 버튼 클릭: UID 저장 + URL 실행
                if (args.TryGetValue(MailWebUrlKey, out var mailWebUrl) &&
                    !string.IsNullOrWhiteSpace(mailWebUrl))
                {
                    OpenMailWebsite(mailWebUrl);
                }
            }
            // 알림 팝업 클릭 (ActionKey 없음 또는 ActionMarkAsRead): UID만 저장 (이미 위에서 저장됨)
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
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// 새 메일 알림 표시
        /// </summary>
        public void ShowNewMail(IReadOnlyList<MailInfo> newMails, string accountKey, string mailWebUrl, string accountName)
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
                    // 단일 메일: 상세 정보 표시 (최대 3줄)
                    var mail = newMails[0];
                    var builder = new ToastContentBuilder()
                        .AddArgument(ActionKey, ActionMarkAsRead)
                        .AddArgument(AccountKeyKey, accountKey)
                        .AddArgument(UidsKey, uidsString)
                        .AddArgument(MailWebUrlKey, mailWebUrl ?? string.Empty)
                        .SetToastDuration(ToastDuration.Long)
                        .AddText(string.Format(Strings.NewMailSingle, Truncate(accountName, 20)))
                        .AddText($"{Truncate(mail.SenderDisplay, MaxSenderLength)}")
                        .AddText($"{Truncate(mail.Subject, MaxSubjectLength)}");

                    // URL이 설정된 경우 버튼 추가
                    if (!string.IsNullOrWhiteSpace(mailWebUrl))
                    {
                        builder.AddButton(new ToastButton()
                            .SetContent(Strings.GoToMail)
                            .AddArgument(ActionKey, ActionGoToMail)
                            .AddArgument(AccountKeyKey, accountKey)
                            .AddArgument(UidsKey, uidsString)
                            .AddArgument(MailWebUrlKey, mailWebUrl));
                    }

                    builder.Show();
                }
                else
                {
                    // 여러 메일: 최신 메일 정보 + 총 개수 (최대 3줄)
                    var latest = newMails.MaxBy(m => m.Date) ?? newMails[0];
                    var builder = new ToastContentBuilder()
                        .AddArgument(ActionKey, ActionMarkAsRead)
                        .AddArgument(AccountKeyKey, accountKey)
                        .AddArgument(UidsKey, uidsString)
                        .AddArgument(MailWebUrlKey, mailWebUrl ?? string.Empty)
                        .SetToastDuration(ToastDuration.Long)
                        .AddText(string.Format(Strings.NewMailMultiple, Truncate(accountName, 20), newMails.Count))
                        .AddText(string.Format(Strings.NewMailLatest, Truncate(latest.SenderDisplay, MaxSenderLength)))
                        .AddText($"{Truncate(latest.Subject, MaxSubjectLength)}");

                    // URL이 설정된 경우 버튼 추가
                    if (!string.IsNullOrWhiteSpace(mailWebUrl))
                    {
                        builder.AddButton(new ToastButton()
                            .SetContent(Strings.GoToMail)
                            .AddArgument(ActionKey, ActionGoToMail)
                            .AddArgument(AccountKeyKey, accountKey)
                            .AddArgument(UidsKey, uidsString)
                            .AddArgument(MailWebUrlKey, mailWebUrl));
                    }

                    builder.Show();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[알림 오류] Toast 알림 표시 실패: {ex.GetType().Name} - {ex.Message}");
            }
        }

        public void ShowError(string message)
        {
            try
            {
                new ToastContentBuilder()
                    .AddText(Strings.MailCheckError)
                    .AddText(Truncate(message, 100))
                    .Show();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[알림 오류] 오류 Toast 표시 실패: {ex.GetType().Name} - {ex.Message}");
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
