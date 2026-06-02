using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace GenShin_Launcher_Plus.Helper
{
    public static class WebPHelper
    {
        public static BitmapSource? LoadImage(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                Logger.Warn("LoadImage: file not found: " + filePath, "BG");
                return null;
            }

            string ext = Path.GetExtension(filePath).ToLower();
            Logger.Debug("LoadImage: " + Path.GetFileName(filePath) + " (" + ext + ", " + new FileInfo(filePath).Length + " bytes)", "BG");

            if (ext == ".webp")
                return DecodeWebP(filePath);

            // Other formats: standard WPF
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                Logger.Debug("LoadImage: OK via BitmapImage (" + bitmap.PixelWidth + "x" + bitmap.PixelHeight + ")", "BG");
                return bitmap;
            }
            catch (Exception ex)
            {
                Logger.Warn("BitmapImage failed: " + ex.Message + ", trying SkiaSharp", "BG");
                return DecodeViaSkia(filePath);
            }
        }

        private static BitmapSource? DecodeWebP(string filePath)
        {
            try
            {
                using var stream = File.OpenRead(filePath);
                using var codec = SKCodec.Create(stream);
                if (codec == null)
                {
                    Logger.Warn("SKCodec.Create returned null for: " + filePath, "BG");
                    return null;
                }
                Logger.Debug("WebP decoded: " + codec.Info.Width + "x" + codec.Info.Height + " " + codec.EncodedFormat, "BG");

                var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888);
                using var bitmap = new SKBitmap(info);
                var result = codec.GetPixels(bitmap.Info, bitmap.GetPixels());
                if (result != SKCodecResult.Success)
                {
                    Logger.Warn("SKCodec.GetPixels failed: " + result, "BG");
                    return null;
                }

                var wpf = SKBitmapToBitmapSource(bitmap);
                Logger.Debug("WebP converted to BitmapSource OK", "BG");
                return wpf;
            }
            catch (Exception ex)
            {
                Logger.Warn("WebP decode exception: " + ex.GetType().Name + ": " + ex.Message, "BG");
                return null;
            }
        }

        private static BitmapSource? DecodeViaSkia(string filePath)
        {
            try
            {
                using var stream = File.OpenRead(filePath);
                using var codec = SKCodec.Create(stream);
                if (codec == null) return null;

                var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888);
                using var bitmap = new SKBitmap(info);
                codec.GetPixels(bitmap.Info, bitmap.GetPixels());
                return SKBitmapToBitmapSource(bitmap);
            }
            catch (Exception ex)
            {
                Logger.Warn("SkiaSharp fallback failed: " + ex.Message, "BG");
                return null;
            }
        }

        private static BitmapSource SKBitmapToBitmapSource(SKBitmap bitmap)
        {
            var wb = new WriteableBitmap(
                bitmap.Width, bitmap.Height, 96, 96,
                PixelFormats.Bgra32, null);
            wb.Lock();
            unsafe
            {
                var src = (byte*)bitmap.GetPixels().ToPointer();
                var dst = (byte*)wb.BackBuffer.ToPointer();
                int size = bitmap.Width * bitmap.Height * 4;
                Buffer.MemoryCopy(src, dst, size, size);
            }
            wb.AddDirtyRect(new System.Windows.Int32Rect(0, 0, bitmap.Width, bitmap.Height));
            wb.Unlock();
            wb.Freeze();
            return wb;
        }
    }
}