using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using MailTrayNotifier.Resources;
using MailTrayNotifier.ViewModels;
using MailTrayNotifier.Views;
using Wpf.Ui.Controls;

namespace MailTrayNotifier
{
    /// <summary>
    /// 앱 메인 창 (설정 화면)
    /// </summary>
    public partial class MainWindow : FluentWindow
    {
        // 페이지 캐시 (매번 새로 생성하지 않음)
        private readonly Dictionary<Type, Page> _pageCache = new();
        private bool _forceClose;
        private Type? _currentPageType;

        public SettingsViewModel ViewModel { get; }

        public MainWindow()
        {
            InitializeComponent();

            var app = App.Instance ?? (App)Application.Current;
            ViewModel = new SettingsViewModel(app.SettingsService, app.MailPollingService, app.MailClientService, app.MailStateStore);
            ViewModel.CloseRequested += OnCloseRequested;
            ViewModel.LanguageChanged += OnLanguageChanged;
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
            DataContext = ViewModel;

            Loaded += OnLoaded;
            Closing += OnClosing;
        }

        private void OnCloseRequested() => Hide();

        /// <summary>
        /// ViewModel 속성 변경 시 About 아이콘 업데이트
        /// </summary>
        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SettingsViewModel.IsUpdateAvailable))
            {
                UpdateAboutIcon();
            }
        }

        /// <summary>
        /// About 메뉴 아이콘 업데이트 (업데이트 가능 시 CloudArrowDown24, 아니면 Info24)
        /// </summary>
        private void UpdateAboutIcon()
        {
            var iconName = ViewModel.IsUpdateAvailable ? Wpf.Ui.Controls.SymbolRegular.CloudArrowDown24 : Wpf.Ui.Controls.SymbolRegular.Info24;

            if (MenuAbout.Content is StackPanel stack && stack.Children[0] is Wpf.Ui.Controls.SymbolIcon icon)
            {
                icon.Symbol = iconName;
            }
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;

            // 오른쪽 하단 작업 표시줄 위쪽에 표시 (오른쪽 20px, 아래쪽 20px 여백)
            const int margin = 20;
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - margin;
            Top = workArea.Bottom - Height - margin;

            await ViewModel.InitializeAsync();

            // 첫 페이지 로드
            NavigateToPage(typeof(MailSettingsPage));
        }

        private void NavMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender == MenuMail)
            {
                NavigateToPage(typeof(MailSettingsPage));
            }
            else if (sender == MenuSettings)
            {
                NavigateToPage(typeof(GeneralSettingsPage));
            }
            else if (sender == MenuAbout)
            {
                NavigateToPage(typeof(AboutPage));
            }
        }

        private void NavigateToPage(Type pageType)
        {
            // 캐시에서 페이지 가져오거나 새로 생성
            if (!_pageCache.TryGetValue(pageType, out var page))
            {
                page = (Page)Activator.CreateInstance(pageType)!;
                page.DataContext = ViewModel;
                _pageCache[pageType] = page;
            }

            _currentPageType = pageType;
            ContentFrame.Navigate(page);

            // 저널 기록 제거 (메모리 누적 방지)
            while (ContentFrame.CanGoBack)
            {
                ContentFrame.RemoveBackEntry();
            }
        }

        /// <summary>
        /// 언어 변경 시 UI 갱신
        /// </summary>
        private void OnLanguageChanged()
        {
            // MainWindow의 정적 텍스트 갱신
            Title = Strings.AppTitle;
            TitleText.Text = Strings.AppTitle;
            NavMailText.Text = Strings.NavMail;
            NavSettingsText.Text = Strings.NavSettings;
            NavAboutText.Text = Strings.About;

            // 페이지 캐시 클리어 후 현재 페이지 재로드
            _pageCache.Clear();
            if (_currentPageType is not null)
            {
                NavigateToPage(_currentPageType);
            }

            // 트레이 메뉴 텍스트 갱신
            App.Instance?.RefreshTrayMenuLanguage();
        }

        /// <summary>
        /// 앱 종료 시 강제 닫기
        /// </summary>
        public void ForceClose()
        {
            _forceClose = true;
            ViewModel.CloseRequested -= OnCloseRequested;
            ViewModel.LanguageChanged -= OnLanguageChanged;
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            ViewModel.Dispose();
            Closing -= OnClosing;
            _pageCache.Clear();
            Close();
        }

        /// <summary>
        /// 창 닫기 시 숨기기 (트레이 상주 유지)
        /// </summary>
        private void OnClosing(object? sender, CancelEventArgs e)
        {
            if (_forceClose)
            {
                return;
            }

            e.Cancel = true;

            // 미저장 신규 계정 제거
            ViewModel.RemoveUnsavedAccounts();

            Hide();
        }
    }
}
