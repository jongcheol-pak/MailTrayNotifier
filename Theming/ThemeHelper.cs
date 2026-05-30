using Microsoft.UI;
using Microsoft.UI.Xaml;

namespace MailTrayNotifier.WinUI.Theming
{
    /// <summary>
    /// 앱 테마 적용 헬퍼 (WinUI ElementTheme 기반).
    /// 메인 창 루트 요소의 RequestedTheme과 타이틀바 캡션 버튼 색상을 변경해 런타임 테마 전환을 지원한다.
    /// </summary>
    internal static class ThemeHelper
    {
        /// <summary>
        /// 테마 코드("dark"/"light"/그 외=시스템)에 따라 메인 창 루트 테마와 타이틀바 색상을 적용한다.
        /// 메인 창이 아직 없으면 무시(창 생성 후 재호출).
        /// </summary>
        public static void Apply(string themeCode)
        {
            var theme = themeCode switch
            {
                "dark" => ElementTheme.Dark,
                "light" => ElementTheme.Light,
                _ => ElementTheme.Default,
            };

            if (App.Instance?.MainWindowContent is not FrameworkElement root)
            {
                return;
            }

            root.RequestedTheme = theme;
            ApplyTitleBarColors(root);
        }

        /// <summary>
        /// 커스텀 타이틀바(ExtendsContentIntoTitleBar) 환경에서 캡션 버튼 전경색을
        /// 실제 적용 테마(ActualTheme)에 맞춰 설정한다. 콘텐츠 루트만 테마가 바뀌고
        /// 타이틀바 버튼 색은 따라오지 않아 글리프가 묻히던 문제를 방지한다.
        /// </summary>
        private static void ApplyTitleBarColors(FrameworkElement root)
        {
            var titleBar = App.Instance?.MainAppWindow?.TitleBar;
            if (titleBar is null)
            {
                return;
            }

            // ElementTheme.Default도 ActualTheme로 해석해 흑/백 선택
            var foreground = root.ActualTheme == ElementTheme.Dark ? Colors.White : Colors.Black;

            titleBar.ButtonForegroundColor = foreground;
            titleBar.ButtonHoverForegroundColor = foreground;
            titleBar.ButtonInactiveForegroundColor = foreground;
        }
    }
}
