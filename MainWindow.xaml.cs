using System.ComponentModel;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MailTrayNotifier.Resources;
using MailTrayNotifier.ViewModels;
using MailTrayNotifier.WinUI.Views;
using WinUIEx;
using Windows.Graphics;

namespace MailTrayNotifier.WinUI
{
    /// <summary>
    /// 앱 메인 창 (설정 화면). 닫기 시 숨겨 트레이 상주를 유지한다.
    /// </summary>
    public sealed partial class MainWindow : WindowEx
    {
        // 네비게이션 태그 → 페이지 매핑
        private static readonly Dictionary<string, Type> PageMap = new()
        {
            ["mail"] = typeof(MailSettingsPage),
            ["settings"] = typeof(GeneralSettingsPage),
            ["about"] = typeof(AboutPage),
        };

        // 페이지 캐시 (매번 새로 생성하지 않음)
        private readonly Dictionary<Type, Page> _pageCache = new();
        private bool _forceClose;
        private bool _initialized;

        public SettingsViewModel ViewModel { get; }

        public MainWindow()
        {
            InitializeComponent();

            // 작업 표시줄/창 미리보기(썸네일)에 표시될 창 아이콘 설정
            AppWindow.SetIcon("Assets/appicon.ico");

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            // 네비/타이틀 텍스트를 현재 언어로 설정 (언어는 앱 시작 시 ApplyStartupSettings에서 적용됨)
            Title = Strings.AppTitle;
            TitleTextBlock.Text = Strings.AppTitle;
            MailItem.Content = Strings.NavMail;
            SettingsItem.Content = Strings.NavSettings;
            AboutItem.Content = Strings.About;

            var app = App.Instance!;
            ViewModel = new SettingsViewModel(
                app.SettingsService, app.MailPollingService, app.MailClientService,
                app.MailStateStore, app.UpdateCheckService);
            ViewModel.CloseRequested += OnCloseRequested;
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;

            // 첫 페이지 선택 (SelectionChanged에서 네비게이션 수행)
            NavView.SelectedItem = MailItem;

            Activated += OnActivated;
            AppWindow.Closing += OnClosing;
        }

        private void OnCloseRequested() => this.Hide();

        /// <summary>
        /// 최초 활성화 시 우하단 배치 + ViewModel 초기화 (1회)
        /// </summary>
        private async void OnActivated(object sender, WindowActivatedEventArgs e)
        {
            // 비활성화(Deactivated) 전이에서는 초기화하지 않는다 (활성화 시점 1회 보장)
            if (_initialized || e.WindowActivationState == WindowActivationState.Deactivated)
            {
                return;
            }
            _initialized = true;

            try
            {
                PositionBottomRight();
                await ViewModel.InitializeAsync();
            }
            catch (Exception ex)
            {
                // 초기화 실패 시 재활성화 때 다시 시도하도록 플래그 복구
                _initialized = false;
                System.Diagnostics.Debug.WriteLine($"창 초기화 실패: {ex.Message}");
            }
        }

        private void PositionBottomRight()
        {
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            var work = area.WorkArea;
            var size = AppWindow.Size;
            const int margin = 20;

            var x = work.X + work.Width - size.Width - margin;
            var y = work.Y + work.Height - size.Height - margin;
            AppWindow.Move(new PointInt32(x, y));
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is NavigationViewItem { Tag: string tag } &&
                PageMap.TryGetValue(tag, out var pageType))
            {
                NavigateToPage(pageType);
            }
        }

        private void NavigateToPage(Type pageType)
        {
            if (!_pageCache.TryGetValue(pageType, out var page))
            {
                page = (Page)Activator.CreateInstance(pageType)!;
                page.DataContext = ViewModel;
                _pageCache[pageType] = page;
            }

            ContentFrame.Content = page;
        }

        /// <summary>
        /// ViewModel 속성 변경 시 정보 항목 업데이트 아이콘 갱신
        /// </summary>
        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SettingsViewModel.IsUpdateAvailable))
            {
                // 업데이트 가능: 다운로드(E896), 아니면 정보(E946)
                AboutItem.Icon = new FontIcon { Glyph = ViewModel.IsUpdateAvailable ? "\uE896" : "\uE946" };
            }
        }

        /// <summary>
        /// 앱 종료 시 강제 닫기
        /// </summary>
        public void ForceClose()
        {
            _forceClose = true;
            ViewModel.CloseRequested -= OnCloseRequested;
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            ViewModel.Dispose();
            AppWindow.Closing -= OnClosing;
            Activated -= OnActivated;
            _pageCache.Clear();
            Close();
        }

        /// <summary>
        /// 창 닫기 시 숨기기 (트레이 상주 유지)
        /// </summary>
        private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            if (_forceClose)
            {
                return;
            }

            args.Cancel = true;

            // 미저장 신규 계정 제거
            ViewModel.RemoveUnsavedAccounts();

            this.Hide();
        }
    }
}
