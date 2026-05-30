using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GenShin_Launcher_Plus.ViewModels;

namespace GenShin_Launcher_Plus
{
    public partial class MainWindow : Window
    {
        public MainWindowViewModel ViewModel { get; }
        private bool _sidebarExpanded;
        private bool _navigatingBack;

        public MainWindow()
        {
            InitializeComponent();
            App.Current.ThisMainWindow = this;
            ViewModel = new MainWindowViewModel(this);
            DataContext = ViewModel;

            // Default size = 50% of screen, unless config overrides
            double cfgW = App.Current.DataModel.MainWidth;
            double cfgH = App.Current.DataModel.MainHeight;
            double screenW = SystemParameters.PrimaryScreenWidth;
            double screenH = SystemParameters.PrimaryScreenHeight;
            if (cfgW <= 0) cfgW = screenW * 0.5;
            if (cfgH <= 0) cfgH = screenH * 0.5;
            Width = cfgW;
            Height = cfgH;
        }

        private void WindowDragMove(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void ContentArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_sidebarExpanded && ViewModel.CurrentPage == null)
            {
                ToggleSidebar(sender, e);
                e.Handled = true;
            }
        }

        public void NavigateBack()
        {
            if (ViewModel.CurrentPage == null || _navigatingBack) return;
            _navigatingBack = true;

            var duration = TimeSpan.FromMilliseconds(250);
            var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };

            var translate = new TranslateTransform();
            PageContent.RenderTransform = translate;
            PageContent.RenderTransformOrigin = new Point(0.5, 0.5);

            var pageFade = new DoubleAnimation(1, 0, duration) { EasingFunction = easing, FillBehavior = FillBehavior.Stop };
            var slideOut = new DoubleAnimation(0, 80, duration) { EasingFunction = easing, FillBehavior = FillBehavior.Stop };
            var overlayFade = new DoubleAnimation(1, 0, duration) { EasingFunction = easing, FillBehavior = FillBehavior.Stop };

            pageFade.Completed += (s, e) =>
            {
                PageContent.Opacity = 1;
                PageContent.RenderTransform = null;
                PageOverlay.Opacity = 1;

                ViewModel.NavigateTo(null);
                ViewModel.RefreshNavVisibility();
                _navigatingBack = false;
            };

            PageContent.BeginAnimation(UIElement.OpacityProperty, pageFade);
            translate.BeginAnimation(TranslateTransform.XProperty, slideOut);
            PageOverlay.BeginAnimation(UIElement.OpacityProperty, overlayFade);
        }

        private void ToggleSidebar(object sender, RoutedEventArgs e)
        {
            _sidebarExpanded = !_sidebarExpanded;
            double from = _sidebarExpanded ? 48 : 220;
            double to = _sidebarExpanded ? 220 : 48;

            var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };

            if (_sidebarExpanded)
            {
                CollapsedPanel.Visibility = Visibility.Collapsed;
                CollapsedBottom.Visibility = Visibility.Collapsed;
                                ExpandedPanel.Visibility = Visibility.Visible;
            }
            else
            {
                ExpandedPanel.Visibility = Visibility.Collapsed;
                CollapsedPanel.Visibility = Visibility.Visible;
                CollapsedBottom.Visibility = Visibility.Visible;
                                NavigateBack();
            }

            Sidebar.BeginAnimation(FrameworkElement.WidthProperty, anim);
        }
    }
}