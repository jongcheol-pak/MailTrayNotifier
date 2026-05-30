using System.Runtime.InteropServices;

namespace MailTrayNotifier.WinUI.Dialogs
{
    /// <summary>메시지 박스 버튼 구성 (WPF 호환)</summary>
    internal enum MessageBoxButton
    {
        OK = 0x0,
        OKCancel = 0x1,
        YesNoCancel = 0x3,
        YesNo = 0x4,
    }

    /// <summary>메시지 박스 아이콘 (WPF 호환)</summary>
    internal enum MessageBoxImage
    {
        None = 0x0,
        Error = 0x10,
        Question = 0x20,
        Warning = 0x30,
        Information = 0x40,
    }

    /// <summary>메시지 박스 결과 (WPF 호환)</summary>
    internal enum MessageBoxResult
    {
        None = 0,
        OK = 1,
        Cancel = 2,
        Yes = 6,
        No = 7,
    }

    /// <summary>
    /// Win32 MessageBox 래퍼 (WPF System.Windows.MessageBox 시그니처 호환, 동기 호출).
    /// 창 유무와 무관하게 동작하므로 트레이 상주 앱의 모든 경로에서 사용 가능.
    /// </summary>
    internal static class MessageBox
    {
        public static MessageBoxResult Show(
            string text,
            string caption = "",
            MessageBoxButton button = MessageBoxButton.OK,
            MessageBoxImage image = MessageBoxImage.None)
        {
            var hwnd = App.Instance?.MainWindowHandle ?? nint.Zero;
            var type = (uint)button | (uint)image;
            // .resx에 리터럴("\n" 2글자)로 저장된 줄바꿈 표기를 실제 줄바꿈으로 변환
            var displayText = text.Replace("\\n", "\n");
            var result = MessageBoxW(hwnd, displayText, caption, type);
            return (MessageBoxResult)result;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBoxW(nint hWnd, string lpText, string lpCaption, uint uType);
    }
}
