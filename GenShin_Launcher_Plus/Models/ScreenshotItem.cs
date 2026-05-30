using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace GenShin_Launcher_Plus.Models
{
    public class ScreenshotItem : INotifyPropertyChanged
    {
        public string FilePath { get; }
        public string FileName { get; }
        public DateTime SaveTime { get; }

        private BitmapImage? _thumbnail;
        public BitmapImage? Thumbnail
        {
            get => _thumbnail;
            private set
            {
                _thumbnail = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasThumbnail));
                OnPropertyChanged(nameof(PlaceholderVisible));
            }
        }

        public bool HasThumbnail => _thumbnail != null;
        public Visibility PlaceholderVisible => _thumbnail == null ? Visibility.Visible : Visibility.Collapsed;

        public ScreenshotItem(string filePath)
        {
            FilePath = filePath;
            FileName = Path.GetFileName(filePath);
            SaveTime = File.GetLastWriteTime(filePath);
        }

        public void LoadThumbnail(int decodeWidth)
        {
            if (_thumbnail != null) return;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(FilePath, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bmp.DecodePixelWidth = decodeWidth;
                bmp.EndInit();
                bmp.Freeze();
                Thumbnail = bmp;
            }
            catch
            {
                // Corrupt or locked file; skip silently.
            }
        }

        public void UnloadThumbnail()
        {
            Thumbnail = null;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}