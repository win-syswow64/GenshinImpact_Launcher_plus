using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using GenShin_Launcher_Plus.Models;
using GenShin_Launcher_Plus.ViewModels;

namespace GenShin_Launcher_Plus.Views
{
    public partial class ScreenshotsPage : UserControl
    {
        private readonly ScreenshotsPageViewModel _vm;
        private ScrollViewer? _scrollViewer;
        private bool _initialized;

        public ScreenshotsPage()
        {
            InitializeComponent();
            _vm = new ScreenshotsPageViewModel();
            DataContext = _vm;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_initialized) return;
            _initialized = true;

            _vm.BackCommand = new RelayCommand(() =>
                App.Current.ThisMainWindow.NavigateBack());

            var lang = App.Current.Language;
            _vm.Title = lang?.NavScreenshotsText ?? "Screenshots";
            _vm.BackToolTip = lang?.BackToolTip ?? "Back";
            _vm.LoadingText = lang?.DownProgress ?? "Loading...";
            _vm.EmptyText = lang?.ScreenPathErr ?? "No screenshots found";

            var screenshotPath = System.IO.Path.Combine(
                App.Current.DataModel.GamePath, "ScreenShot");
            await _vm.LoadScreenshotsAsync(screenshotPath);

            _scrollViewer = FindScrollViewer(ImageListBox);
            if (_scrollViewer != null)
            {
                _scrollViewer.ScrollChanged += OnScrollChanged;
                UpdateVisibleRange();
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _vm?.UnloadAll();
            if (_scrollViewer != null)
                _scrollViewer.ScrollChanged -= OnScrollChanged;
        }

        private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            UpdateVisibleRange();
        }

        private void UpdateVisibleRange()
        {
            if (_scrollViewer == null || _vm.Groups.Count == 0) return;

            double viewportHeight = _scrollViewer.ViewportHeight;
            double verticalOffset = _scrollViewer.VerticalOffset;
            double extentHeight = _scrollViewer.ExtentHeight;

            if (extentHeight <= 0) return;

            double scrollCenter = verticalOffset + viewportHeight / 2;
            int centerGroup = (int)(scrollCenter / extentHeight * _vm.Groups.Count);
            centerGroup = Math.Max(0, Math.Min(centerGroup, _vm.Groups.Count - 1));

            _vm.UpdateVisibleRange(centerGroup, _vm.Groups.Count);
        }

        // --- Image preview overlay ---

        private void Thumbnail_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            if (fe.DataContext is not ScreenshotItem item) return;

            try
            {
                // Load a high-res version for the overlay
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(item.FilePath, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bmp.DecodePixelWidth = 1920;
                bmp.EndInit();
                bmp.Freeze();

                OverlayImage.Source = bmp;
                OverlayFileName.Text = item.FileName;
                OverlayFileDate.Text = item.SaveTime.ToString("yyyy-MM-dd HH:mm:ss");
                ImageOverlay.Visibility = Visibility.Visible;
                ImageOverlay.Focus();
            }
            catch
            {
                // File may be locked; fall back to thumbnail
                OverlayImage.Source = item.Thumbnail;
                OverlayFileName.Text = item.FileName;
                OverlayFileDate.Text = item.SaveTime.ToString("yyyy-MM-dd HH:mm:ss");
                ImageOverlay.Visibility = Visibility.Visible;
                ImageOverlay.Focus();
            }

            e.Handled = true;
        }

        private void ImageOverlay_Click(object sender, MouseButtonEventArgs e)
        {
            CloseOverlay();
        }

        private void ImageOverlay_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CloseOverlay();
                e.Handled = true;
            }
        }

        private void CloseOverlay()
        {
            ImageOverlay.Visibility = Visibility.Collapsed;
            OverlayImage.Source = null;
        }

        // --- Helpers ---

        private static ScrollViewer? FindScrollViewer(DependencyObject parent)
        {
            if (parent is ScrollViewer sv) return sv;
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                var result = FindScrollViewer(child);
                if (result != null) return result;
            }
            return null;
        }
    }
}