using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GenShin_Launcher_Plus.Helper;
using GenShin_Launcher_Plus.Models;
using GenShin_Launcher_Plus.Service.IService;
using GenShin_Launcher_Plus.ViewModels;
using Newtonsoft.Json;

namespace GenShin_Launcher_Plus.Service
{
    public class MainService : IMainWindowService
    {
        public MainService(MainWindow main, MainWindowViewModel vm)
        {
            CheckConfig(main);
            _ = MainBackgroundLoadAsync(vm);
        }

        public async Task CheckNotice()
        {
            Logger.Debug("Checking for notices", "Main");
            string json = await HtmlHelper.GetInfoFromHtmlAsync("Notice");
            App.Current.NoticeObject = JsonConvert.DeserializeObject<NoticeModel>(json) ?? new();
        }

        public async Task MainBackgroundLoadAsync(MainWindowViewModel vm)
        {
            App.Current.IsLoadingBackground = true;
            Logger.Debug("Loading background", "Main");
            var bg = new ImageBrush { Stretch = Stretch.UniformToFill };
            var defaultUri = new Uri("pack://application:,,,/Images/MainBackground.jpg", UriKind.Absolute);

            var bgPath = App.Current.DataModel.BackgroundPath;
            if (!string.IsNullOrEmpty(bgPath) && File.Exists(bgPath))
            {
                // Custom background from local file
                using var fs = File.OpenRead(bgPath);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = fs;
                bitmap.EndInit();
                bg.ImageSource = bitmap;
            }
            else if (App.Current.DataModel.UseXunkongWallpaper)
            {
                // Daily image toggle ON: load from API (with local cache)
                bg.ImageSource = new BitmapImage(defaultUri);
                try
                {
                    App.Current.BackgroundModel ??= new BackgroundModel();
                    string directUrl = await HtmlHelper.GetDailyImageDirectUrlAsync();
                    if (!string.IsNullOrEmpty(directUrl))
                    {
                        App.Current.BackgroundModel.BackgroundUrl = directUrl;

                        using var client = new HttpClient(new HttpClientHandler
                        {
                            AutomaticDecompression = DecompressionMethods.All
                        });
                        client.DefaultRequestHeaders.UserAgent.ParseAdd(
                            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                        var bytes = await client.GetByteArrayAsync(directUrl);

                        var configDir = Path.Combine(AppContext.BaseDirectory, "Config");
                        if (!Directory.Exists(configDir))
                            Directory.CreateDirectory(configDir);
                        var wallpaperPath = Path.Combine(configDir, "Wallpaper.jpg");
                        File.WriteAllBytes(wallpaperPath, bytes);

                        var ms = new MemoryStream(bytes);
                        var newBitmap = new BitmapImage();
                        newBitmap.BeginInit();
                        newBitmap.CacheOption = BitmapCacheOption.OnLoad;
                        newBitmap.StreamSource = ms;
                        newBitmap.EndInit();
                        bg.ImageSource = newBitmap;
                    }
                }
                catch
                {
                    bg.ImageSource = new BitmapImage(defaultUri);
                }
            }
            else
            {
                // Daily image toggle OFF: load MiHoYo background
                bg.ImageSource = new BitmapImage(defaultUri);
                try
                {
                    App.Current.BackgroundModel ??= new BackgroundModel();
                    App.Current.BackgroundModel.BackgroundUrl = await HtmlHelper.GetBackgroundImageUrlAsync();
                    string bgUrl = App.Current.BackgroundModel.BackgroundUrl;
                    if (!string.IsNullOrEmpty(bgUrl) && bgUrl != "null")
                    {
                        using var client = new HttpClient(new HttpClientHandler
                        {
                            AutomaticDecompression = DecompressionMethods.All
                        });
                        var bytes = await client.GetByteArrayAsync(bgUrl);
                        var ms = new MemoryStream(bytes);
                        var newBitmap = new BitmapImage();
                        newBitmap.BeginInit();
                        newBitmap.CacheOption = BitmapCacheOption.OnLoad;
                        newBitmap.StreamSource = ms;
                        newBitmap.EndInit();
                        bg.ImageSource = newBitmap;
                    }
                }
                catch
                {
                    bg.ImageSource = new BitmapImage(defaultUri);
                }
            }

            Logger.Debug("Background loaded successfully", "Main");
            vm.Background = bg;
            Application.Current.Dispatcher.Invoke(() =>
            {
                App.Current.ThisMainWindow.BackgroundImage.ImageSource = bg.ImageSource;
            });
            App.Current.IsLoadingBackground = false;
        }

        public void CheckConfig(MainWindow main)
        {
            if (!Directory.Exists("UserData"))
                Directory.CreateDirectory("UserData");

            var game = App.Current.DataModel.ActiveGame;
            var gamePath = App.Current.DataModel.GamePath ?? "";
            if (game == null) return;
            if (!File.Exists(Path.Combine(gamePath, game.CnExeName)) &&
                !File.Exists(Path.Combine(gamePath, game.GlobalExeName)))
            {
                Logger.Info("No game path configured, showing guide page", "Main");
                main.MainGrid.Children.Add(new Views.GuidePage());
            }
        }
    }
}