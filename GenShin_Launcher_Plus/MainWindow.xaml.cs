using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using GenShin_Launcher_Plus.ViewModels;
using System.Runtime.InteropServices;
using System.Threading;
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

            Closed += (_, _) => StopVideoPlayback();
        }

        private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left || IsInteractiveTopBarElement(e.OriginalSource as DependencyObject))
                return;

            try { DragMove(); } catch { }
        }

        private static bool IsInteractiveTopBarElement(DependencyObject? source)
        {
            while (source != null)
            {
                if (source is ButtonBase or Selector or TextBoxBase or RangeBase)
                    return true;
                source = VisualTreeHelper.GetParent(source);
            }
            return false;
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
            var requestId = Interlocked.Increment(ref _backgroundRequestId);
            try
            {
                StopVideoPlayback();
                RestoreRootBackground();
                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {
                    _ = SetBackgroundImageAsync(filePath, requestId);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Failed to set background image: " + ex.Message, "BG");
                SetBackgroundResource("pack://application:,,,/Images/MainBackground.jpg");
            }
        }

        private async Task SetBackgroundImageAsync(string filePath, long requestId)
        {
            try
            {
                BitmapSource? bitmap = await Task.Run(() => WebPHelper.LoadImage(filePath));
                if (requestId != Volatile.Read(ref _backgroundRequestId)) return;

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
            catch (Exception ex)
            {
                if (requestId == Volatile.Read(ref _backgroundRequestId))
                {
                    Logger.Warn("Failed to load background image: " + ex.Message, "BG");
                    SetBackgroundResource("pack://application:,,,/Images/MainBackground.jpg");
                }
            }
        }

        public void SetBackgroundResource(string uriString)
        {
            Interlocked.Increment(ref _backgroundRequestId);
            try
            {
                StopVideoPlayback();
                RestoreRootBackground();
                BackgroundImage.Source = new BitmapImage(new Uri(uriString, UriKind.RelativeOrAbsolute));
                BackgroundImage.Opacity = 1;
            }
            catch { }
        }

        public void SetBackgroundVideo(string videoPath, string? overlayImagePath = null, string? fallbackImagePath = null)
        {
            Interlocked.Increment(ref _backgroundRequestId);
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

                var ext = Path.GetExtension(videoPath).ToLowerInvariant();
                if (ext is ".webm" or ".mkv")
                {
                    Logger.Debug("Using in-window VLC background for " + ext + ": " + videoPath, "BG");
                    PlayVlcBackground(videoPath, fallbackImagePath);
                    return;
                }

                // Try WPF MediaElement first (GPU-accelerated via Media Foundation + DXVA2)
                RestoreRootBackground();
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

        // ==================== Background: Video (LibVLCSharp software rendering) ====================

        private LibVLC? _libVLC;
        private LibVLCSharp.Shared.MediaPlayer? _vlcPlayer;
        private string? _videoFallbackPath;
        private WriteableBitmap? _videoBitmap;
        private long _backgroundRequestId;
        private readonly object _vlcBufferLock = new();
        private readonly List<IntPtr> _retiredVlcBuffers = new();
        private IntPtr _vlcWriteBuffer;
        private IntPtr _vlcReadyBuffer;
        private int _vlcBufferSize;
        private int _vlcFrameStride;
        private int _vlcFrameWidth;
        private int _vlcFrameHeight;
        private int _vlcFrameUpdatePending;
        private long _lastVlcFrameTick;

        private LibVLC GetOrCreateLibVLC()
        {
            if (_libVLC == null)
            {
                _libVLC = new LibVLC("--no-video-title-show", "--quiet", "--no-stats", "--avcodec-hw=any", "--drop-late-frames", "--skip-frames", "--file-caching=1000");
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
                _videoUsingMediaElement = false;
                _mediaElementFailed = false;

                // Stop MediaElement (safe, UI thread)
                try { BackgroundVideo.Stop(); } catch { }
                BackgroundVideo.Source = null;
                BackgroundVideo.Opacity = 0;

                // Stop VLC: signal callbacks to skip before disposing the player.
                var player = _vlcPlayer;
                if (player != null)
                {
                    _vlcStopping = true;
                    _vlcPlayer = null;
                    player.EndReached -= VlcPlayer_EndReached;
                    player.EncounteredError -= VlcPlayer_Error;
                    // Dispose on background thread to avoid deadlock with VLC callbacks.
                    Task.Run(() => { try { player.Stop(); } catch { } try { player.Dispose(); } catch { } });
                }
                try { _videoBitmap?.Unlock(); } catch { }
                _videoBitmap = null;
                _vlcFrameUpdatePending = 0;
                VideoFrameImage.Source = null;
                VideoFrameImage.Opacity = 0;
                VideoOverlayImage.Opacity = 0;
            }
            catch { }
        }

        private void RestoreRootBackground()
        {
            RootChrome.Background = TryFindResource("AppBackgroundBrush") as Brush
                ?? new SolidColorBrush(Color.FromRgb(0x11, 0x12, 0x16));
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
            FallbackToVlcOrStatic(_lastVideoPath ?? "", _videoFallbackPath);
        }

        private string? _lastVideoPath;

        private void FallbackToVlcOrStatic(string videoPath, string? fallbackPath)
        {
            Logger.Debug("Falling back to in-window VLC background", "BG");
            PlayVlcBackground(videoPath, fallbackPath);
        }

        private void PlayVlcBackground(string videoPath, string? fallbackPath)
        {
            try
            {
                if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath))
                {
                    if (!string.IsNullOrEmpty(fallbackPath)) SetBackgroundImage(fallbackPath);
                    return;
                }

                RestoreRootBackground();
                BackgroundImage.Opacity = 0;
                BackgroundVideo.Source = null;
                BackgroundVideo.Opacity = 0;
                VideoFrameImage.Source = null;
                VideoFrameImage.Opacity = 0;

                _videoUsingMediaElement = false;
                _vlcStopping = false;
                _lastVlcFrameTick = 0;
                Interlocked.Exchange(ref _vlcFrameUpdatePending, 0);
                _videoFallbackPath = fallbackPath;
                _lastVideoPath = videoPath;

                var libVLC = GetOrCreateLibVLC();
                var player = new LibVLCSharp.Shared.MediaPlayer(libVLC)
                {
                    Mute = true,
                    EnableHardwareDecoding = true,
                };
                player.SetVideoFormatCallbacks(VlcFormat, VlcCleanup);
                player.SetVideoCallbacks(VlcLock, VlcUnlock, VlcDisplay);
                player.EndReached += VlcPlayer_EndReached;
                player.EncounteredError += VlcPlayer_Error;
                _vlcPlayer = player;

                using var media = CreateVlcMedia(libVLC, videoPath);
                player.Play(media);
                Logger.Debug("VLC in-window background playback started", "BG");
            }
            catch (Exception ex)
            {
                Logger.Warn("VLC in-window background failed: " + ex.Message, "BG");
                StopVideoPlayback();
                RestoreRootBackground();
                if (!string.IsNullOrEmpty(fallbackPath)) SetBackgroundImage(fallbackPath);
            }
        }

        private Media CreateVlcMedia(LibVLC libVLC, string videoPath)
        {
            var media = new Media(libVLC, new Uri(videoPath));
            media.AddOption(":no-video-title-show");
            media.AddOption(":avcodec-hw=any");
            media.AddOption(":file-caching=1000");
            media.AddOption(":drop-late-frames");
            media.AddOption(":skip-frames");
            media.AddOption(":no-audio");
            media.AddOption(":input-repeat=65535");
            return media;
        }

        // VLC video callbacks for software rendering (IntPtr-based delegates).
        // VLC writes into one unmanaged buffer while WPF copies the last completed frame from another.
        private volatile bool _vlcStopping;

        private IntPtr VlcLock(IntPtr opaque, IntPtr planes)
        {
            lock (_vlcBufferLock)
            {
                unsafe { if (planes != IntPtr.Zero) ((IntPtr*)planes.ToPointer())[0] = _vlcWriteBuffer; }
            }
            return IntPtr.Zero;
        }

        private void VlcUnlock(IntPtr opaque, IntPtr picture, IntPtr planes)
        {
            if (_vlcStopping) return;
            if (_videoBitmap == null || _vlcWriteBuffer == IntPtr.Zero) return;

            var now = Environment.TickCount64;
            var last = Interlocked.Read(ref _lastVlcFrameTick);
            if (now - last < 33) return;
            Interlocked.Exchange(ref _lastVlcFrameTick, now);

            if (Interlocked.Exchange(ref _vlcFrameUpdatePending, 1) == 1) return;
            lock (_vlcBufferLock)
            {
                (_vlcReadyBuffer, _vlcWriteBuffer) = (_vlcWriteBuffer, _vlcReadyBuffer);
            }

            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    if (_vlcStopping || _videoBitmap == null) return;
                    lock (_vlcBufferLock)
                    {
                        if (_vlcReadyBuffer == IntPtr.Zero || _vlcBufferSize <= 0) return;
                        _videoBitmap.WritePixels(
                            new System.Windows.Int32Rect(0, 0, _vlcFrameWidth, _vlcFrameHeight),
                            _vlcReadyBuffer,
                            _vlcBufferSize,
                            _vlcFrameStride);
                    }
                }
                catch { }
                finally { Interlocked.Exchange(ref _vlcFrameUpdatePending, 0); }
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private void VlcDisplay(IntPtr opaque, IntPtr picture)
        {
            // Frame already committed in VlcUnlock
        }

        private void VlcCleanup(ref IntPtr opaque)
        {
            Interlocked.Exchange(ref _vlcFrameUpdatePending, 0);
        }

        private uint VlcFormat(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height, ref uint pitches, ref uint lines)
        {
            if (_vlcStopping) return 0;
            unsafe
            {
                byte* c = (byte*)chroma.ToPointer();
                c[0] = (byte)'R'; c[1] = (byte)'V'; c[2] = (byte)'3'; c[3] = (byte)'2';
            }
            pitches = width * 4;
            lines = height;

            int w = (int)width, h = (int)height;
            EnsureVlcFrameBuffers(w, h, (int)pitches);
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

        private void EnsureVlcFrameBuffers(int width, int height, int stride)
        {
            var required = checked(stride * height);
            lock (_vlcBufferLock)
            {
                _vlcFrameWidth = width;
                _vlcFrameHeight = height;
                _vlcFrameStride = stride;
                if (_vlcWriteBuffer != IntPtr.Zero && _vlcReadyBuffer != IntPtr.Zero && _vlcBufferSize >= required)
                    return;

                if (_vlcWriteBuffer != IntPtr.Zero) _retiredVlcBuffers.Add(_vlcWriteBuffer);
                if (_vlcReadyBuffer != IntPtr.Zero) _retiredVlcBuffers.Add(_vlcReadyBuffer);

                _vlcWriteBuffer = Marshal.AllocHGlobal(required);
                _vlcReadyBuffer = Marshal.AllocHGlobal(required);
                _vlcBufferSize = required;
                unsafe
                {
                    new Span<byte>(_vlcWriteBuffer.ToPointer(), required).Clear();
                    new Span<byte>(_vlcReadyBuffer.ToPointer(), required).Clear();
                }
            }
        }

        private void FreeVlcBuffers()
        {
            lock (_vlcBufferLock)
            {
                if (_vlcWriteBuffer != IntPtr.Zero) Marshal.FreeHGlobal(_vlcWriteBuffer);
                if (_vlcReadyBuffer != IntPtr.Zero) Marshal.FreeHGlobal(_vlcReadyBuffer);
                foreach (var buffer in _retiredVlcBuffers)
                    if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                _retiredVlcBuffers.Clear();
                _vlcWriteBuffer = IntPtr.Zero;
                _vlcReadyBuffer = IntPtr.Zero;
                _vlcBufferSize = 0;
            }
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
                        using var media = CreateVlcMedia(_libVLC, _lastVideoPath);
                        _vlcStopping = false;
                        _vlcPlayer.Play(media);
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
                ExpandedPanel.Visibility = Visibility.Visible;
            }
            else
            {
                ExpandedPanel.Visibility = Visibility.Collapsed;
                CollapsedPanel.Visibility = Visibility.Visible;
                NavigateBack();
            }
            Sidebar.BeginAnimation(FrameworkElement.WidthProperty, anim);
        }
    }
}
