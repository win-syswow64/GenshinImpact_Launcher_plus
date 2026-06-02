using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using GenShin_Launcher_Plus.ViewModels;
using System.Threading.Tasks;
using GenShin_Launcher_Plus.Helper;
using LibVLCSharp.Shared;

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
            if (e.ChangedButton == MouseButton.Left) DragMove();
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

        // ==================== Background: Static Image ====================

        public void SetBackgroundImage(string filePath)
        {
            try
            {
                StopVideoPlayback();
                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {
                    BitmapSource bitmap = WebPHelper.LoadImage(filePath);
                    if (bitmap != null)
                    {
                        BackgroundImage.Source = bitmap;
                        BackgroundImage.Opacity = 1;
                        Logger.Debug("BackgroundImage set OK (" + bitmap.PixelWidth + "x" + bitmap.PixelHeight + ")", "BG");
                    }
                    else
                    {
                        Logger.Warn("WebPHelper returned null for: " + filePath + ", using default", "BG");
                        SetBackgroundResource("pack://application:,,,/Images/MainBackground.jpg");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Failed to set background image: " + ex.Message, "BG");
                SetBackgroundResource("pack://application:,,,/Images/MainBackground.jpg");
            }
        }

        public void SetBackgroundResource(string uriString)
        {
            try
            {
                StopVideoPlayback();
                BackgroundImage.Source = new BitmapImage(new Uri(uriString, UriKind.RelativeOrAbsolute));
                BackgroundImage.Opacity = 1;
            }
            catch { }
        }

        // ==================== Background: Video (LibVLCSharp software rendering) ====================

        private LibVLC? _libVLC;
        private LibVLCSharp.Shared.MediaPlayer? _vlcPlayer;
        private string? _videoFallbackPath;
        private WriteableBitmap? _videoBitmap;
        // WriteableBitmap requires UI thread access, synchronized via Dispatcher

        private LibVLC GetOrCreateLibVLC()
        {
            if (_libVLC == null)
            {
                _libVLC = new LibVLC("--no-video-title-show", "--quiet", "--no-stats");
                Logger.Debug("LibVLC initialized: " + _libVLC.Version, "BG");
            }
            return _libVLC;
        }

        private bool _videoUsingMediaElement;
        private bool _mediaElementFailed;

        private void StopVideoPlayback()
        {
            try
            {
                // Stop MediaElement (safe, UI thread)
                BackgroundVideo.Source = null;
                BackgroundVideo.Opacity = 0;

                // Stop VLC: signal callbacks to skip, then stop/dispose on background thread
                if (_vlcPlayer != null)
                {
                    _vlcStopping = true;
                    _vlcPlayer.EndReached -= VlcPlayer_EndReached;
                    _vlcPlayer.EncounteredError -= VlcPlayer_Error;
                    var player = _vlcPlayer;
                    _vlcPlayer = null;
                    // Dispose on background thread to avoid deadlock with VLC callbacks
                    Task.Run(() =>
                    {
                        try { player.Stop(); } catch { }
                        try { player.Dispose(); } catch { }
                    });
                }
                _videoBitmap = null;
                VideoFrameImage.Source = null;
                VideoFrameImage.Opacity = 0;
                VideoOverlayImage.Opacity = 0;
                _vlcStopping = false;
            }
            catch { }
        }

        // MediaElement events (GPU path)
        private void MediaElement_Opened(object sender, RoutedEventArgs e)
        {
            if (!_videoUsingMediaElement) return;
            Logger.Debug("MediaElement opened OK (GPU): " +
                BackgroundVideo.NaturalVideoWidth + "x" + BackgroundVideo.NaturalVideoHeight, "BG");
            BackgroundVideo.Position = TimeSpan.Zero;
            BackgroundVideo.Play();
            BackgroundVideo.Opacity = 1;
            // Hide VLC layer in case it was showing
            VideoFrameImage.Opacity = 0;
        }

        private void MediaElement_Ended(object sender, RoutedEventArgs e)
        {
            if (!_videoUsingMediaElement) return;
            BackgroundVideo.Position = TimeSpan.Zero;
            BackgroundVideo.Play();
        }

        private void MediaElement_Failed(object sender, System.Windows.ExceptionRoutedEventArgs e)
        {
            if (!_videoUsingMediaElement || _mediaElementFailed) return;
            _mediaElementFailed = true;
            string ext = _lastVideoPath != null ? System.IO.Path.GetExtension(_lastVideoPath).ToLower() : "";
            if (ext is ".webm" or ".mkv")
                Logger.Debug("MediaElement does not support " + ext + ", using VLC", "BG");
            else
                Logger.Warn("MediaElement failed: " + e.ErrorException?.Message + ", using VLC", "BG");
            BackgroundVideo.Source = null;
            BackgroundVideo.Opacity = 0;
            FallbackToVlcOrStatic(
                _videoFallbackPath != null ? (_lastVideoPath ?? "") : "",
                _videoFallbackPath);
        }

        private string? _lastVideoPath;

        public void SetBackgroundVideo(string videoPath, string? overlayImagePath = null, string? fallbackImagePath = null)
        {
            try
            {
                StopVideoPlayback();
                if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath))
                {
                    if (!string.IsNullOrEmpty(fallbackImagePath)) SetBackgroundImage(fallbackImagePath);
                    return;
                }
                _videoFallbackPath = fallbackImagePath;
                _lastVideoPath = videoPath;
                _videoUsingMediaElement = false;
                _mediaElementFailed = false;

                // Load theme overlay (WebP) on top of video
                if (!string.IsNullOrEmpty(overlayImagePath) && File.Exists(overlayImagePath))
                {
                    var overlay = WebPHelper.LoadImage(overlayImagePath);
                    if (overlay != null)
                    {
                        VideoOverlayImage.Source = overlay;
                        VideoOverlayImage.Opacity = 1;
                    }
                }

                // Try WPF MediaElement first (GPU-accelerated via Media Foundation + DXVA2)
                BackgroundImage.Opacity = 0;
                _videoUsingMediaElement = true;
                Logger.Debug("Trying MediaElement (GPU): " + videoPath, "BG");
                BackgroundVideo.Source = new Uri(videoPath, UriKind.Absolute);
                BackgroundVideo.Play();
                BackgroundVideo.Opacity = 1;

                // If MediaElement fails, MediaElement_Failed will trigger VLC fallback
            }
            catch (Exception ex)
            {
                Logger.Warn("SetBackgroundVideo exception: " + ex.Message, "BG");
                FallbackToVlcOrStatic(videoPath, fallbackImagePath);
            }
        }

        private void FallbackToVlcOrStatic(string videoPath, string fallbackPath)
        {
            // Try VLC software rendering
            try
            {
                Logger.Debug("Falling back to VLC software render", "BG");
                var vlc = GetOrCreateLibVLC();
                _vlcPlayer = new LibVLCSharp.Shared.MediaPlayer(vlc);
                _vlcPlayer.EndReached += VlcPlayer_EndReached;
                _vlcPlayer.EncounteredError += VlcPlayer_Error;
                _vlcPlayer.SetVideoCallbacks(
                    new LibVLCSharp.Shared.MediaPlayer.LibVLCVideoLockCb(VlcLock),
                    new LibVLCSharp.Shared.MediaPlayer.LibVLCVideoUnlockCb(VlcUnlock),
                    new LibVLCSharp.Shared.MediaPlayer.LibVLCVideoDisplayCb(VlcDisplay));
                _vlcPlayer.SetVideoFormatCallbacks(
                    new LibVLCSharp.Shared.MediaPlayer.LibVLCVideoFormatCb(VlcFormat),
                    null);
                BackgroundImage.Opacity = 0;
                using var media = new Media(vlc, new Uri(videoPath));
                _vlcPlayer.Play(media);
                Logger.Debug("VLC software playback started", "BG");
            }
            catch (Exception ex)
            {
                Logger.Warn("VLC fallback also failed: " + ex.Message, "BG");
                StopVideoPlayback();
                if (!string.IsNullOrEmpty(fallbackPath)) SetBackgroundImage(fallbackPath);
            }
        }

        // VLC video callbacks for software rendering (IntPtr-based delegates)
        // VLC callbacks: Lock/Unlock MUST run on UI thread (WriteableBitmap requirement)
        private System.Threading.ManualResetEventSlim _lockReady = new(false);
        private IntPtr _currentBackBuffer;
        private volatile bool _vlcStopping;

        private IntPtr VlcLock(IntPtr opaque, IntPtr planes)
        {
            if (_vlcStopping) return IntPtr.Zero;
            _lockReady.Reset();
            try
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (_vlcStopping || _videoBitmap == null) { _lockReady.Set(); return; }
                    try
                    {
                        _videoBitmap.Lock();
                        _currentBackBuffer = _videoBitmap.BackBuffer;
                    }
                    catch { _currentBackBuffer = IntPtr.Zero; }
                    _lockReady.Set();
                });
                _lockReady.Wait(200); // timeout to avoid deadlock during shutdown
            }
            catch { }
            unsafe { ((IntPtr*)planes.ToPointer())[0] = _currentBackBuffer; }
            return IntPtr.Zero;
        }

        private void VlcUnlock(IntPtr opaque, IntPtr picture, IntPtr planes)
        {
            // Dispatch Unlock() to UI thread (async to avoid blocking VLC)
            Dispatcher.BeginInvoke(() =>
            {
                if (_videoBitmap != null)
                {
                    try
                    {
                        _videoBitmap.AddDirtyRect(new System.Windows.Int32Rect(
                            0, 0, _videoBitmap.PixelWidth, _videoBitmap.PixelHeight));
                        _videoBitmap.Unlock();
                    }
                    catch { }
                }
            });
        }

        private void VlcDisplay(IntPtr opaque, IntPtr picture)
        {
            // Frame already committed in VlcUnlock
        }

        private uint VlcFormat(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height, ref uint pitches, ref uint lines)
        {
            unsafe
            {
                byte* c = (byte*)chroma.ToPointer();
                c[0] = (byte)'R'; c[1] = (byte)'V'; c[2] = (byte)'3'; c[3] = (byte)'2';
            }
            pitches = width * 4;
            lines = height;

            int w = (int)width, h = (int)height;
            Dispatcher.Invoke(() =>
            {
                _videoBitmap = new WriteableBitmap(w, h, 96, 96,
                    System.Windows.Media.PixelFormats.Bgra32, null);
                VideoFrameImage.Source = _videoBitmap;
                VideoFrameImage.Opacity = 1;
            });

            Logger.Debug("VLC format: " + w + "x" + h + " BGRA", "BG");
            return 1;
        }

        private void VlcPlayer_EndReached(object? sender, EventArgs e)
        {
            // EndReached means current Media is finished; must create a new Media to replay
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    if (_vlcPlayer != null && _libVLC != null && _lastVideoPath != null)
                    {
                        var media = new Media(_libVLC, new Uri(_lastVideoPath));
                        _vlcPlayer.Play(media);
                        media.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn("VLC loop replay failed: " + ex.Message, "BG");
                }
            });
        }

        private void VlcPlayer_Error(object? sender, EventArgs e)
        {
            Logger.Warn("VLC error, falling back to static image", "BG");
            Dispatcher.BeginInvoke(() => { StopVideoPlayback(); ShowFallbackImage(); });
        }

        private void ShowFallbackImage()
        {
            if (!string.IsNullOrEmpty(_videoFallbackPath) && File.Exists(_videoFallbackPath))
            {
                var bmp = WebPHelper.LoadImage(_videoFallbackPath);
                if (bmp != null) { BackgroundImage.Source = bmp; BackgroundImage.Opacity = 1; return; }
            }
            BackgroundImage.Opacity = 1;
        }

        // ==================== Sidebar ====================

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
