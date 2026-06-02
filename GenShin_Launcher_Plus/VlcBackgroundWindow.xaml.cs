using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using GenShin_Launcher_Plus.Helper;
using LibVLCSharp.Shared;

namespace GenShin_Launcher_Plus
{
    public partial class VlcBackgroundWindow : Window
    {
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_SHOWWINDOW = 0x0040;

        private LibVLCSharp.Shared.MediaPlayer? _player;
        private LibVLC? _libVLC;
        private string? _videoPath;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        public VlcBackgroundWindow()
        {
            InitializeComponent();
        }

        public void Play(LibVLC libVLC, string videoPath)
        {
            if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
                return;

            _libVLC = libVLC;
            _videoPath = videoPath;

            StopPlayer();

            _player = new LibVLCSharp.Shared.MediaPlayer(libVLC)
            {
                Mute = true,
                EnableHardwareDecoding = true,
            };
            _player.EndReached += Player_EndReached;
            _player.EncounteredError += Player_EncounteredError;
            VideoView.MediaPlayer = _player;

            using var media = CreateMedia(videoPath);
            _player.Play(media);
            Logger.Debug("VLC external background playback started", "BG");
        }

        public void SyncBehind(Window owner)
        {
            if (owner.WindowState == WindowState.Minimized || string.IsNullOrEmpty(_videoPath))
            {
                Hide();
                return;
            }

            Left = owner.Left;
            Top = owner.Top;
            Width = owner.ActualWidth > 0 ? owner.ActualWidth : owner.Width;
            Height = owner.ActualHeight > 0 ? owner.ActualHeight : owner.Height;

            if (!IsVisible)
                Show();

            var videoHandle = new WindowInteropHelper(this).Handle;
            var ownerHandle = new WindowInteropHelper(owner).Handle;
            if (videoHandle != IntPtr.Zero && ownerHandle != IntPtr.Zero)
            {
                SetWindowPos(videoHandle, ownerHandle, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
        }

        public void StopAndHide()
        {
            StopPlayer();
            Hide();
        }

        protected override void OnClosed(EventArgs e)
        {
            StopPlayer();
            base.OnClosed(e);
        }

        private Media CreateMedia(string path)
        {
            var media = new Media(_libVLC!, new Uri(path));
            media.AddOption(":no-video-title-show");
            media.AddOption(":avcodec-hw=any");
            media.AddOption(":file-caching=1000");
            media.AddOption(":drop-late-frames");
            media.AddOption(":skip-frames");
            media.AddOption(":no-audio");
            return media;
        }

        private void StopPlayer()
        {
            var player = _player;
            if (player == null) return;

            _player = null;
            try { player.EndReached -= Player_EndReached; } catch { }
            try { player.EncounteredError -= Player_EncounteredError; } catch { }
            try { VideoView.MediaPlayer = null; } catch { }
            try { player.Stop(); } catch { }
            try { player.Dispose(); } catch { }
        }

        private void Player_EndReached(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    if (_player == null || _libVLC == null || string.IsNullOrEmpty(_videoPath)) return;
                    using var media = CreateMedia(_videoPath);
                    _player.Play(media);
                }
                catch (Exception ex)
                {
                    Logger.Warn("VLC external loop replay failed: " + ex.Message, "BG");
                }
            });
        }

        private void Player_EncounteredError(object? sender, EventArgs e)
        {
            Logger.Warn("VLC external background error", "BG");
        }
    }
}
