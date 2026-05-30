using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MailTrayNotifier.ViewModels;

namespace MailTrayNotifier.WinUI.Views
{
    /// <summary>
    /// 정보 페이지 (앱 정보/업데이트/오픈소스 라이선스)
    /// </summary>
    public sealed partial class AboutPage : Page
    {
        public AboutPage()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 웹사이트 링크(HyperlinkButton) 클릭 → URL 열기 (Tag에 URL)
        /// </summary>
        private void OnLinkClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string url })
            {
                OpenUrl(url);
            }
        }

        /// <summary>
        /// 라이선스 카드(SettingsCard) 클릭 → 홈페이지 열기 (Tag에 URL)
        /// </summary>
        private void OnLicenseCardClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string url })
            {
                OpenUrl(url);
            }
        }

        private void OpenUrl(string url)
        {
            if (DataContext is SettingsViewModel vm && vm.OpenLicenseUrlCommand.CanExecute(url))
            {
                vm.OpenLicenseUrlCommand.Execute(url);
            }
        }
    }
}
