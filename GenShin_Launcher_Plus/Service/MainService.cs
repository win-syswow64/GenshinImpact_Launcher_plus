using System;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using GenShin_Launcher_Plus.Helper;
using GenShin_Launcher_Plus.Models;
using GenShin_Launcher_Plus.Service.IService;
using GenShin_Launcher_Plus.ViewModels;
using Newtonsoft.Json;

namespace GenShin_Launcher_Plus.Service
{
    public class MainService : IMainWindowService
    {
        private static readonly System.Threading.SemaphoreSlim _bgSemaphore = new(1, 1);
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
            try
            {
                await LoadGameBackgroundAsync();
            }
            catch (Exception ex)
            {
                Logger.Warn("Background load failed: " + ex.Message, "Background");
                Logger.Warn("Stack trace: " + ex.StackTrace, "Background");
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    App.Current.ThisMainWindow.SetBackgroundResource(
                        "pack://application:,,,/Images/MainBackground.jpg");
                });
            }
            App.Current.IsLoadingBackground = false;
        }

        public static async Task LoadGameBackgroundAsync()
        {
            // Wait for any in-progress load to finish, then proceed.
            // Timeout prevents deadlock if the previous load is stuck.
            var entered = await _bgSemaphore.WaitAsync(TimeSpan.FromSeconds(10));
            if (!entered)
            {
                Logger.Warn("BG semaphore timeout, skip stale load", "BG");
                return;
            }
            try
            {
                await LoadGameBackgroundCoreAsync();
            }
            finally
            {
                _bgSemaphore.Release();
            }
        }

        private static async Task LoadGameBackgroundCoreAsync()
        {
            var main = App.Current.ThisMainWindow;
            if (main == null) { Logger.Debug("main is null, skip", "BG"); return; }
            var profile = App.Current.DataModel.ActiveGame;
            if (profile == null) { Logger.Debug("profile is null, skip", "BG"); return; }
            var loadGameId = profile.Id;
            var loadGameBiz = App.Current.DataModel.ActiveGameBiz;
            bool IsStillActive() =>
                App.Current.DataModel.ActiveGameBiz == loadGameBiz &&
                App.Current.DataModel.ActiveGame?.Id == loadGameId;

            Logger.Debug("LoadGameBackground: game=" + profile.Id + " biz=" + loadGameBiz, "BG");

            // 1. Per-game custom background file
            string customBg = App.Current.DataModel.GetCustomBackground(profile.Id);
            if (!string.IsNullOrEmpty(customBg) && File.Exists(customBg))
            {
                Logger.Debug("Using custom background: " + customBg, "BG");
                if (!IsStillActive()) return;
                if (BackgroundService.IsVideoFile(customBg))
                {
                    var playable = await BackgroundService.PreparePlayableVideoAsync(customBg, profile.Id, customBg);
                    if (!IsStillActive()) return;
                    System.Windows.Application.Current.Dispatcher.Invoke(() => main.SetBackgroundVideo(playable));
                }
                else
                    System.Windows.Application.Current.Dispatcher.Invoke(() => main.SetBackgroundImage(customBg));
                return;
            }

            // 2. Legacy global custom background (backward compat)
            string legacyBg = App.Current.DataModel.BackgroundPath;
            if (!string.IsNullOrEmpty(legacyBg) && File.Exists(legacyBg))
            {
                Logger.Debug("Using legacy background: " + legacyBg, "BG");
                if (!IsStillActive()) return;
                if (BackgroundService.IsVideoFile(legacyBg))
                {
                    var playable = await BackgroundService.PreparePlayableVideoAsync(legacyBg, profile.Id, legacyBg);
                    if (!IsStillActive()) return;
                    System.Windows.Application.Current.Dispatcher.Invoke(() => main.SetBackgroundVideo(playable));
                }
                else
                    System.Windows.Application.Current.Dispatcher.Invoke(() => main.SetBackgroundImage(legacyBg));
                return;
            }

            // 3. API backgrounds
            Logger.Debug("Fetching API backgrounds...", "BG");
            var allBgs = await BackgroundService.FetchBackgroundsAsync(profile);
            if (!IsStillActive()) return;
            Logger.Debug("Fetched " + allBgs.Count + " backgrounds from API", "BG");

            if (allBgs.Count == 0)
            {
                Logger.Warn("No backgrounds returned from API", "BG");
                if (!IsStillActive()) return;
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    main.SetBackgroundResource("pack://application:,,,/Images/MainBackground.jpg"));
                return;
            }

            // Log all available backgrounds
            for (int i = 0; i < allBgs.Count; i++)
            {
                var b = allBgs[i];
                string url = b.IsVideo ? (b.Video?.Url ?? "null") : (b.Background?.Url ?? "null");
                Logger.Debug("  bg[" + i + "] id=" + b.Id + " type=" + b.Type + " url=" + url, "BG");
            }

            // Try to cache and display each background in order
            string? cacheFile = null;
            HoYoGameBackground? selected = null;
            foreach (var bg in allBgs)
            {
                string? url = bg.IsVideo ? bg.Video?.Url : bg.Background?.Url;
                if (string.IsNullOrEmpty(url) || url.StartsWith("pack:"))
                {
                    Logger.Debug("  skip bg id=" + bg.Id + " (no url)", "BG");
                    continue;
                }
                Logger.Debug("  trying bg id=" + bg.Id + " url=" + url, "BG");
                cacheFile = await BackgroundService.CacheBackgroundFileAsync(url, profile.Id);
                if (!IsStillActive()) return;
                if (cacheFile != null)
                {
                    selected = bg;
                    Logger.Debug("  cached: " + cacheFile, "BG");
                    break;
                }
                Logger.Debug("  cache returned null, trying next", "BG");
            }

            if (cacheFile == null || selected == null)
            {
                Logger.Warn("All backgrounds failed to cache, using default", "BG");
                if (!IsStillActive()) return;
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    main.SetBackgroundResource("pack://application:,,,/Images/MainBackground.jpg"));
                return;
            }

            // Display the background
            if (selected.IsVideo)
            {
                // Cache the theme overlay (for video overlay)
                string? themePath = null;
                if (selected.Theme != null && !string.IsNullOrEmpty(selected.Theme.Url))
                    themePath = await BackgroundService.CacheBackgroundFileAsync(selected.Theme.Url, profile.Id);
                if (!IsStillActive()) return;
                // Cache the static fallback image (for when video can't play, e.g. WebM)
                string? fallbackPath = null;
                if (selected.Background != null && !string.IsNullOrEmpty(selected.Background.Url))
                    fallbackPath = await BackgroundService.CacheBackgroundFileAsync(selected.Background.Url, profile.Id);
                if (!IsStillActive()) return;
                string playbackPath = await BackgroundService.PreparePlayableVideoAsync(cacheFile, profile.Id, selected.Video?.Url);
                if (!IsStillActive()) return;
                Logger.Debug("Setting VIDEO bg: video=" + playbackPath + " source=" + cacheFile + " theme=" + themePath + " fallback=" + fallbackPath, "BG");
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    main.SetBackgroundVideo(playbackPath, themePath, fallbackPath));
                BackgroundService.CleanupUnusedBackgroundCache(profile.Id, new[] { cacheFile, themePath, fallbackPath, playbackPath });
            }
            else
            {
                Logger.Debug("Setting IMAGE background: " + cacheFile, "BG");
                if (!IsStillActive()) return;
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    main.SetBackgroundImage(cacheFile));
                BackgroundService.CleanupUnusedBackgroundCache(profile.Id, new[] { cacheFile });
            }

            if (!selected.IsCustom && !string.IsNullOrEmpty(selected.Id))
                App.Current.DataModel.SetSelectedBackgroundId(profile.Id, selected.Id);

            Logger.Debug("Background loaded OK: " + selected.Id, "BG");
        }

        public void CheckConfig(MainWindow main)
        {
            if (!Directory.Exists("UserData"))
                Directory.CreateDirectory("UserData");

            var game = App.Current.DataModel.ActiveGame;
            if (game == null) return;
            var gamePath = App.Current.DataModel.GamePath ?? "";
            if (!File.Exists(Path.Combine(gamePath, game.CnExeName)) &&
                !File.Exists(Path.Combine(gamePath, game.GlobalExeName)))
            {
                Logger.Info("Game path not configured, running auto-search", "Main");
                var biz = App.Current.DataModel.ActiveBiz;
                var found = GameSearchService.FindGame(game, biz.Server, allowDifferentServer: true);
                if (found != null)
                {
                    var gameBiz = $"{game.Id}_{found.Server}";
                    Logger.Info($"Auto-found game path: {found.Path} ({gameBiz})", "Main");
                    App.Current.DataModel.ActiveGameBiz = gameBiz;
                    App.Current.DataModel.SetGamePath(gameBiz, found.Path);
                    App.Current.DataModel.SaveDataToFile();
                }
                else
                {
                    Logger.Info("Auto-search found nothing, user can set path in settings", "Main");
                }
            }
        }
    }
}
