using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MailTrayNotifier.ViewModels;

namespace MailTrayNotifier.WinUI.Views
{
    /// <summary>
    /// 메일 계정 설정 페이지
    /// </summary>
    public sealed partial class MailSettingsPage : Page
    {
        public MailSettingsPage()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 포트 입력란: 숫자만 허용
        /// </summary>
        private void Port_BeforeTextChanging(TextBox sender, TextBoxBeforeTextChangingEventArgs args)
        {
            args.Cancel = !args.NewText.All(char.IsDigit);
        }

        private void EditAccount_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: MailAccountViewModel account })
            {
                account.BeginEdit();
            }
        }

        private void CancelEdit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: MailAccountViewModel account })
            {
                if (DataContext is SettingsViewModel viewModel)
                {
                    viewModel.CancelAccountEdit(account);
                }
                else
                {
                    account.CancelEdit();
                }
            }
        }

        private async void SaveEdit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: MailAccountViewModel account } &&
                DataContext is SettingsViewModel viewModel)
            {
                await viewModel.SaveAccountAsync(account);
            }
        }

        private void DeleteAccount_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: MailAccountViewModel account } &&
                DataContext is SettingsViewModel viewModel &&
                viewModel.RemoveAccountCommand.CanExecute(account))
            {
                viewModel.RemoveAccountCommand.Execute(account);
            }
        }
    }
}
