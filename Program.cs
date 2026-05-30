using System.Threading;
using MailTrayNotifier.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace MailTrayNotifier.WinUI
{
    /// <summary>
    /// 커스텀 진입점. AppInstance로 단일 인스턴스를 보장하고, 두 번째 실행(일반/알림 클릭)은
    /// 기존 인스턴스로 활성화를 리디렉션한다. (알림 클릭 시 "이미 실행 중" 중복 표시 방지)
    /// </summary>
    public static class Program
    {
        private const string InstanceKey = "MailTrayNotifier_SingleInstance";

        [STAThread]
        private static int Main(string[] args)
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();

            if (RedirectToExistingInstanceIfNeeded())
            {
                // 기존 인스턴스로 활성화를 넘겼으므로 이 프로세스는 종료
                return 0;
            }

            // 표시 언어(.resw/x:Uid)는 XAML이 처음 로드되기 전에 적용해야 첫 재시작에 바로 반영된다.
            // (OnLaunched 시점에 설정하면 ResourceContext가 이미 고정되어 한 박자 늦게 적용됨)
            ApplyStartupLanguageOverride();

            Application.Start(p =>
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                _ = new App();
            });

            return 0;
        }

        /// <summary>
        /// 저장된 언어 코드를 XAML 로드 전에 PrimaryLanguageOverride로 적용한다.
        /// 빈 값(시스템 기본)이면 빈 문자열로 override를 해제한다.
        /// </summary>
        private static void ApplyStartupLanguageOverride()
        {
            try
            {
                var (languageCode, _) = SettingsService.LoadStartupSettingsSync();
                Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = languageCode ?? string.Empty;
            }
            catch
            {
                // 언어 적용 실패는 시작을 막지 않는다 (시스템 기본 언어로 진행)
            }
        }

        /// <summary>
        /// 이미 실행 중인 인스턴스가 있으면 활성화를 리디렉션하고 true를 반환한다.
        /// 현재 프로세스가 첫 인스턴스면 Activated 이벤트를 구독하고 false를 반환한다.
        /// </summary>
        private static bool RedirectToExistingInstanceIfNeeded()
        {
            var keyInstance = AppInstance.FindOrRegisterForKey(InstanceKey);

            if (keyInstance.IsCurrent)
            {
                // 첫 인스턴스: 이후 다른 인스턴스의 활성화를 이 인스턴스에서 처리
                keyInstance.Activated += OnActivated;
                return false;
            }

            // 두 번째 인스턴스: 활성화 인자를 첫 인스턴스로 넘기고 종료
            var activatedArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
            keyInstance.RedirectActivationToAsync(activatedArgs).AsTask().Wait();
            return true;
        }

        private static void OnActivated(object? sender, AppActivationArguments args)
        {
            App.Instance?.OnRedirectedActivation(args);
        }
    }
}
