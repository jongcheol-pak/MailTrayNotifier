using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace MailTrayNotifier.WinUI.Converters
{
    /// <summary>
    /// bool → Visibility 변환 (true=Visible, false=Collapsed). WinUI 기본 미제공이라 자체 구현.
    /// </summary>
    public sealed class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => value is true ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => value is Visibility.Visible;
    }
}
