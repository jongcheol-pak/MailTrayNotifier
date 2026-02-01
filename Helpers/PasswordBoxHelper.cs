namespace MailTrayNotifier.Helpers
{
    /// <summary>
    /// PasswordBox 바인딩을 위한 헬퍼
    /// </summary>
    public static class PasswordBoxHelper
    {
        public static readonly DependencyProperty BoundPasswordProperty =
            DependencyProperty.RegisterAttached(
                "BoundPassword",
                typeof(string),
                typeof(PasswordBoxHelper),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnBoundPasswordChanged));

        public static readonly DependencyProperty BindPasswordProperty =
            DependencyProperty.RegisterAttached(
                "BindPassword",
                typeof(bool),
                typeof(PasswordBoxHelper),
                new PropertyMetadata(false, OnBindPasswordChanged));

        private static readonly DependencyProperty UpdatingPasswordProperty =
            DependencyProperty.RegisterAttached(
                "UpdatingPassword",
                typeof(bool),
                typeof(PasswordBoxHelper),
                new PropertyMetadata(false));

        public static string GetBoundPassword(DependencyObject obj)
        {
            return (string)obj.GetValue(BoundPasswordProperty);
        }

        public static void SetBoundPassword(DependencyObject obj, string value)
        {
            obj.SetValue(BoundPasswordProperty, value);
        }

        public static bool GetBindPassword(DependencyObject obj)
        {
            return (bool)obj.GetValue(BindPasswordProperty);
        }

        public static void SetBindPassword(DependencyObject obj, bool value)
        {
            obj.SetValue(BindPasswordProperty, value);
        }

        private static bool GetUpdatingPassword(DependencyObject obj)
        {
            return (bool)obj.GetValue(UpdatingPasswordProperty);
        }

        private static void SetUpdatingPassword(DependencyObject obj, bool value)
        {
            obj.SetValue(UpdatingPasswordProperty, value);
        }

        private static void OnBindPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not PasswordBox passwordBox)
            {
                return;
            }

            if ((bool)e.OldValue)
            {
                passwordBox.PasswordChanged -= HandlePasswordChanged;
            }

            if ((bool)e.NewValue)
            {
                passwordBox.PasswordChanged += HandlePasswordChanged;
            }
        }

        private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not PasswordBox passwordBox)
            {
                return;
            }

            if (GetUpdatingPassword(passwordBox))
            {
                return;
            }

            passwordBox.Password = e.NewValue?.ToString() ?? string.Empty;
        }

        private static void HandlePasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is not PasswordBox passwordBox)
            {
                return;
            }

            SetUpdatingPassword(passwordBox, true);
            SetBoundPassword(passwordBox, passwordBox.Password);
            SetUpdatingPassword(passwordBox, false);
        }
    }
}
