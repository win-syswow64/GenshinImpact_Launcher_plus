using System;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using GenShin_Launcher_Plus.Core;
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
            MainBackgroundLoad(vm);
        }

        /// <summary>
        /// 异步实现Main中的背景调用
        /// </summary>
        /// <param name="vm"></param>
        public async void MainBackgroundLoad(MainWindowViewModel vm)
        {
            App.Current.IsLoadingBackground = true;
            vm.Background = new ImageBrush();
            if (App.Current.DataModel.UseXunkongWallpaper)
            {
                vm.Background.Stretch = Stretch.UniformToFill;
                var uri = new Uri("pack://application:,,,/Images/MainBackground.jpg", UriKind.Absolute);
                var file = Path.Combine(AppContext.BaseDirectory, "Config/Wallpaper.jpg");
                if (File.Exists(file))
                {
                    uri = new Uri(file);
                    using var fs = File.OpenRead(file);
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = fs;
                    bitmap.EndInit();
                    vm.Background.ImageSource = bitmap;
                }
                else
                {
                    vm.Background.ImageSource = new BitmapImage(uri);
                }

                //string imageJson = await HtmlHelper.GetInfoFromHtmlAsync("Image");
                //DailyImageModel dailyImage = JsonConvert.DeserializeObject<DailyImageModel>(imageJson);
                //if (dailyImage == null)
                //{
                //    MessageBox.Show("DailyImage Json returns error: object is null");
                //    App.Current.IsLoadingBackground = false;
                //    return;
                //}

                //int year = DateTime.Now.Year;
                //int month = DateTime.Now.Month;
                //int day = DateTime.Now.Day;
                //string imageDate = $"{year}{month}{day}";
                //int count = dailyImage.ImageInfo.FindIndex(t => t.ImageDate == imageDate);
                //if (count == -1) count = 0;
                //if (dailyImage.ImageInfo[count].ImageDate != App.Current.DataModel.ImageDate || !File.Exists(file))
                //{
                //    try
                //    {
                //        string url = $"https://pixiv.re/{dailyImage.ImageInfo[count].ImagePid}.jpg";
                //        var client = new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All });
                //        var bytes = await client.GetByteArrayAsync(url);
                //        var ms = new MemoryStream(bytes);
                //        var newBitmap = new BitmapImage();
                //        newBitmap.BeginInit();
                //        newBitmap.CacheOption = BitmapCacheOption.OnLoad;
                //        newBitmap.StreamSource = ms;
                //        newBitmap.EndInit();
                //        vm.Background.ImageSource = newBitmap;
                //        await File.WriteAllBytesAsync(file, bytes);
                //        App.Current.DataModel.ImageDate = dailyImage.ImageInfo[count].ImageDate;
                //    }
                //    catch (Exception ex)
                //    {
                //        App.Current.IsLoadingBackground = false;
                //        MessageBox.Show($"PID:{dailyImage.ImageInfo[count].ImagePid}\r\n{ex.Message}");
                //    }
                //}
            }
            else
            {
                Uri uri = new("pack://application:,,,/Images/MainBackground.jpg", UriKind.Absolute);
                if (!App.Current.DataModel.IsWebBg)
                {
                    vm.Background.ImageSource = new BitmapImage(uri);
                    if (App.Current?.BackgroundModel != null)
                    {
                        App.Current.BackgroundModel.BackgroundUrl = await HtmlHelper.GetBackgroundImageUrlAsync();
                    }
                    else
                    {
                        // 初始化 PkgUpdataModel 或处理 null 情况
                        App.Current.BackgroundModel = new BackgroundModel();
                        App.Current.BackgroundModel.BackgroundUrl = await HtmlHelper.GetBackgroundImageUrlAsync();
                    }
                    string bgurl = App.Current.BackgroundModel.BackgroundUrl;
                    var client = new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All });
                    if (bgurl != null & bgurl != "null" && bgurl != string.Empty)
                    {
                        try
                        {
                            var bytes = await client.GetByteArrayAsync(bgurl);
                            var ms = new MemoryStream(bytes);
                            var newBitmap = new BitmapImage();
                            newBitmap.BeginInit();
                            newBitmap.CacheOption = BitmapCacheOption.OnLoad;
                            newBitmap.StreamSource = ms;
                            newBitmap.EndInit();
                            vm.Background.ImageSource = newBitmap;
                            vm.Background.Stretch = Stretch.UniformToFill;
                        }
                        catch (Exception e)
                        {
                            MessageBox.Show($"下载图片失败!");
                            vm.Background.ImageSource = new BitmapImage(uri);
                            vm.Background.Stretch = Stretch.UniformToFill;
                            Console.WriteLine($"Request error: {e.Message}");
                        }
                    }
                    App.Current.IsLoadingBackground = false;
                    return;
                }

                vm.Background.ImageSource = new BitmapImage(uri);
                vm.Background.Stretch = Stretch.UniformToFill;

                var bg = App.Current.DataModel.BackgroundPath;
                if (File.Exists(bg))
                {
                    using var fs = File.OpenRead(bg);
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = fs;
                    bitmap.EndInit();
                    vm.Background.ImageSource = bitmap;
                }
            }
            App.Current.IsLoadingBackground = false;
            App.Current.ThisMainWindow.Height = Convert.ToDouble(App.Current.DataModel.MainHeight);
            App.Current.ThisMainWindow.Width = Convert.ToDouble(App.Current.DataModel.MainWidth);
        }

        /// <summary>
        /// 检查程序所需要的配置文件
        /// </summary>
        /// <param name="main"></param>
        public void CheckConfig(MainWindow main)
        {
            if (!Directory.Exists(@"UserData"))
            {
                Directory.CreateDirectory("UserData");
            }
            if (!File.Exists(Path.Combine(App.Current.DataModel.GamePath ?? "Err", "Yuanshen.exe")) &&
                !File.Exists(Path.Combine(App.Current.DataModel.GamePath ?? "Err", "GenshinImpact.exe")))
            {
                main.MainGrid.Children.Add(new Views.GuidePage());
            }
        }
    }
}
