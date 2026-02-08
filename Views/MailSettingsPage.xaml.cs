using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MailTrayNotifier.ViewModels;

namespace MailTrayNotifier.Views
{
    public partial class MailSettingsPage : Page
    {
        private static readonly Regex NumericRegex = new("^[0-9]+$", RegexOptions.Compiled);

        public MailSettingsPage()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 숫자만 입력 허용
        /// </summary>
        private void NumericTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !NumericRegex.IsMatch(e.Text);
        }

        /// <summary>
        /// 붙여넣기 시 숫자만 허용
        /// </summary>
        private void NumericTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(typeof(string)))
            {
                var text = (string)e.DataObject.GetData(typeof(string));
                if (!NumericRegex.IsMatch(text ?? string.Empty))
                {
                    e.CancelCommand();
                }
            }
            else
            {
                e.CancelCommand();
            }
        }

        /// <summary>
        /// 계정 편집 시작
        /// </summary>
        private void EditAccount_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is MailAccountViewModel account)
            {
                account.BeginEdit();
            }
        }

        /// <summary>
        /// 계정 편집 취소
        /// </summary>
        private void CancelEdit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is MailAccountViewModel account)
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

        /// <summary>
        /// 계정 편집 저장
        /// </summary>
        private async void SaveEdit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is MailAccountViewModel account)
            {
                if (DataContext is SettingsViewModel viewModel)
                {
                    await viewModel.SaveAccountAsync(account);
                }
                else
                {
                    account.EndEdit();
                }
            }
        }
    }
}
