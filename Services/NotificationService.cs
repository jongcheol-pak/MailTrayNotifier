using System.Diagnostics;
using MailTrayNotifier.Models;
using MailTrayNotifier.Resources;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace MailTrayNotifier.Services
{
    /// <summary>
    /// Windows 알림 표시 서비스 (Windows App SDK AppNotificationManager 기반)
    /// </summary>
    public sealed class NotificationService
    {
        private const int MaxSubjectLength = 100;
        private const int MaxSenderLength = 50;
        private const string ActionKey = "action";
        private const string ActionMarkAsRead = "markAsRead";
        private const string ActionGoToMail = "goToMail";
        private const string ActionOpenUpdate = "openUpdate";
        private const string UidsKey = "uids";
        private const string AccountKeyKey = "accountKey";
        private const string MailWebUrlKey = "mailWebUrl";
        private const string UpdateUrlKey = "updateUrl";

        /// <summary>
        /// 알림 클릭 시 UID 저장 요청 이벤트
        /// </summary>
        public event Action<string, IReadOnlyList<string>>? SaveUidsRequested;

        /// <summary>
        /// 알림 관리자 등록 및 활성화 이벤트 구독
        /// </summary>
        public void Initialize()
        {
            var manager = AppNotificationManager.Default;
            manager.NotificationInvoked += OnNotificationInvoked;
            manager.Register();
        }

        /// <summary>
        /// 앱 종료 시 알림 등록 해제 및 기록 정리
        /// </summary>
        public void Shutdown()
        {
            try
            {
                var manager = AppNotificationManager.Default;
                manager.NotificationInvoked -= OnNotificationInvoked;
                AppNotificationManager.Default.UnregisterAll();
            }
            catch
            {
                // 정리 실패 무시
            }
        }

        /// <summary>
        /// 알림 클릭/버튼 활성화 이벤트 핸들러 (앱 실행 중 직접 활성화)
        /// </summary>
        private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
            => HandleActivation(args);

        /// <summary>
        /// 알림 클릭/버튼 활성화 처리.
        /// 단일 인스턴스 리디렉션 경로(AppInstance.Activated)에서도 호출할 수 있도록 public.
        /// </summary>
        public void HandleActivation(AppNotificationActivatedEventArgs args)
            => HandleArguments(args.Arguments);

        /// <summary>
        /// 레거시 토스트 활성화(ToastNotificationActivatedEventArgs.Argument, query string 형태) 처리.
        /// </summary>
        public void HandleActivation(string toastArgument)
            => HandleArguments(ParseQueryString(toastArgument));

        private void HandleArguments(IDictionary<string, string> arguments)
        {
            // 업데이트 알림 처리
            if (arguments.TryGetValue(ActionKey, out var actionValue) && actionValue == ActionOpenUpdate)
            {
                if (arguments.TryGetValue(UpdateUrlKey, out var updateUrl) &&
                    !string.IsNullOrWhiteSpace(updateUrl))
                {
                    OpenMailWebsite(updateUrl);
                }
                return;
            }

            if (!arguments.TryGetValue(AccountKeyKey, out var accountKey) ||
                !arguments.TryGetValue(UidsKey, out var uidsString))
            {
                return;
            }

            var uids = uidsString.Split(',', StringSplitOptions.RemoveEmptyEntries);

            // UID 저장 (모든 경우에 저장)
            SaveUidsRequested?.Invoke(accountKey, uids);

            // 액션에 따라 추가 동작 수행
            if (arguments.TryGetValue(ActionKey, out var action) && action == ActionGoToMail)
            {
                // 버튼 클릭: UID 저장 + URL 실행
                if (arguments.TryGetValue(MailWebUrlKey, out var mailWebUrl) &&
                    !string.IsNullOrWhiteSpace(mailWebUrl))
                {
                    OpenMailWebsite(mailWebUrl);
                }
            }
            // 알림 팝업 클릭 (ActionKey 없음 또는 ActionMarkAsRead): UID만 저장 (이미 위에서 저장됨)
        }

        /// <summary>
        /// AppNotification 인자 문자열("key=value;key2=value2")을 딕셔너리로 파싱한다.
        /// AppNotificationBuilder는 세미콜론(;)으로 인자를 구분한다.
        /// </summary>
        private static IDictionary<string, string> ParseQueryString(string query)
        {
            var dict = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(query))
            {
                return dict;
            }

            foreach (var pair in query.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var idx = pair.IndexOf('=');
                if (idx > 0)
                {
                    var key = pair[..idx];
                    var value = pair[(idx + 1)..];
                    dict[key] = value;
                }
            }
            return dict;
        }

        /// <summary>
        /// 메일/업데이트 웹사이트 열기 (패키지 앱 표준: Launcher.LaunchUriAsync)
        /// </summary>
        private static async void OpenMailWebsite(string url)
        {
            try
            {
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    await Windows.System.Launcher.LaunchUriAsync(uri);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[알림] URL 열기 실패: {url} - {ex.Message}");
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

                AppNotificationBuilder builder;
                if (newMails.Count == 1)
                {
                    // 단일 메일: 상세 정보 표시 (최대 3줄)
                    var mail = newMails[0];
                    builder = new AppNotificationBuilder()
                        .AddArgument(ActionKey, ActionMarkAsRead)
                        .AddArgument(AccountKeyKey, accountKey)
                        .AddArgument(UidsKey, uidsString)
                        .AddArgument(MailWebUrlKey, mailWebUrl ?? string.Empty)
                        .SetDuration(AppNotificationDuration.Long)
                        .AddText(string.Format(Strings.NewMailSingle, Truncate(accountName, 20)))
                        .AddText($"{Truncate(mail.SenderDisplay, MaxSenderLength)}({mail.Date:yy-MM-dd HH:mm})")
                        .AddText($"{Truncate(mail.Subject, MaxSubjectLength)}");
                }
                else
                {
                    // 여러 메일: 최신 메일 정보 + 총 개수 (최대 3줄)
                    var latest = newMails.MaxBy(m => m.Date) ?? newMails[0];
                    builder = new AppNotificationBuilder()
                        .AddArgument(ActionKey, ActionMarkAsRead)
                        .AddArgument(AccountKeyKey, accountKey)
                        .AddArgument(UidsKey, uidsString)
                        .AddArgument(MailWebUrlKey, mailWebUrl ?? string.Empty)
                        .SetDuration(AppNotificationDuration.Long)
                        .AddText(string.Format(Strings.NewMailMultiple, Truncate(accountName, 20), newMails.Count))
                        .AddText(string.Format(Strings.NewMailLatest, Truncate(latest.SenderDisplay, MaxSenderLength)))
                        .AddText($"{Truncate(latest.Subject, MaxSubjectLength)}");
                }

                // URL이 설정된 경우 버튼 추가
                if (!string.IsNullOrWhiteSpace(mailWebUrl))
                {
                    builder.AddButton(new AppNotificationButton(Strings.GoToMail)
                        .AddArgument(ActionKey, ActionGoToMail)
                        .AddArgument(AccountKeyKey, accountKey)
                        .AddArgument(UidsKey, uidsString)
                        .AddArgument(MailWebUrlKey, mailWebUrl));
                }

                AppNotificationManager.Default.Show(builder.BuildNotification());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[알림 오류] 알림 표시 실패: {ex.GetType().Name} - {ex.Message}");
            }
        }

        /// <summary>
        /// 업데이트 가능 알림 표시
        /// </summary>
        public void ShowUpdateAvailable(string latestVersion, string currentVersion, string releaseUrl)
        {
            try
            {
                var builder = new AppNotificationBuilder()
                    .AddArgument(ActionKey, ActionOpenUpdate)
                    .AddArgument(UpdateUrlKey, releaseUrl)
                    .AddText(Strings.UpdateAvailableTitle)
                    .AddText(string.Format(Strings.UpdateAvailableMessage, latestVersion, currentVersion));

                if (!string.IsNullOrWhiteSpace(releaseUrl))
                {
                    builder.AddButton(new AppNotificationButton(Strings.UpdateButton)
                        .AddArgument(ActionKey, ActionOpenUpdate)
                        .AddArgument(UpdateUrlKey, releaseUrl));
                }

                AppNotificationManager.Default.Show(builder.BuildNotification());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[알림 오류] 업데이트 알림 표시 실패: {ex.GetType().Name} - {ex.Message}");
            }
        }

        /// <summary>
        /// 오류 알림 표시
        /// </summary>
        public void ShowError(string message)
        {
            try
            {
                var builder = new AppNotificationBuilder()
                    .AddText(Strings.MailCheckError)
                    .AddText(Truncate(message, 100));

                AppNotificationManager.Default.Show(builder.BuildNotification());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[알림 오류] 오류 알림 표시 실패: {ex.GetType().Name} - {ex.Message}");
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
