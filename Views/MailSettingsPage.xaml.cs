using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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
    }
}
