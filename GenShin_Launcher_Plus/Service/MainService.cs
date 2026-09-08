using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using GenShin_Launcher_Plus.Helper;
using GenShin_Launcher_Plus.Core;
using GenShin_Launcher_Plus.Models;
using GenShin_Launcher_Plus.Service.IService;
using GenShin_Launcher_Plus.ViewModels;
using Newtonsoft.Json;

namespace GenShin_Launcher_Plus.Service
{
    public class MainService : IMainWindowService
    {
        private static readonly System.Threading.SemaphoreSlim _bgSemaphore = new(1, 1);
        private static readonly object _backgroundCancellationLock = new();
        private static CancellationTokenSource? _backgroundLoadCts;
        private static long _backgroundLoadGeneration;
        private readonly ILauncherSession _session;

        public MainService(ILauncherSession session, MainWindow main, MainWindowViewModel vm)
        {
            _session = session;
            _ = InitializeAsync(main, vm);
        }

        private async Task InitializeAsync(MainWindow main, MainWindowViewModel vm)
        {
            try
            {
                bool pathsChanged = await CheckConfigAsync(main);
                if (pathsChanged)
                    vm.RefreshGameSelector(reloadBackground: false);
                await MainBackgroundLoadAsync(vm);
            }
            catch (Exception ex)
            {
                Logger.Warn("Launcher initialization failed: " + ex.Message, "Main");
            }
        }

        public async Task CheckNotice()
        {
            Logger.Debug("Checking for notices", "Main");
            string json = await HtmlHelper.GetInfoFromHtmlAsync("Notice");
            _session.Notice = JsonConvert.DeserializeObject<NoticeModel>(json) ?? new();
        }

        public async Task MainBackgroundLoadAsync(MainWindowViewModel vm)
        {
            long generation = Interlocked.Increment(ref _backgroundLoadGeneration);
            _session.IsLoadingBackground = true;
            Logger.Debug("Loading background", "Main");
            try
            {
                await LoadGameBackgroundAsync();
            }
            catch (OperationCanceledException)
            {
                Logger.Debug("Background load canceled", "Background");
            }
            catch (Exception ex)
            {
                Logger.Warn("Background load failed: " + ex.Message, "Background");
                Logger.Warn("Stack trace: " + ex.StackTrace, "Background");
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    _session.MainWindow?.SetBackgroundResource(
                        "pack://application:,,,/Images/MainBackground.jpg");
                });
            }
            finally
            {
                // A canceled predecessor must not hide the loading state of
                // the newer request that replaced it.
                if (Volatile.Read(ref _backgroundLoadGeneration) == generation)
                    _session.IsLoadingBackground = false;
            }
        }

        public async Task LoadGameBackgroundAsync(CancellationToken cancellationToken = default)
        {
            CancellationTokenSource loadCts;
            lock (_backgroundCancellationLock)
            {
                _backgroundLoadCts?.Cancel();
                loadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _backgroundLoadCts = loadCts;
            }

            var entered = false;
            try
            {
                // The newest request cancels the old conversion/download and
                // then waits for it to release the shared coordinator. Never
                // discard the newest background request on an arbitrary wait.
                await _bgSemaphore.WaitAsync(loadCts.Token);
                entered = true;

                await LoadGameBackgroundCoreAsync(loadCts.Token);
            }
            finally
            {
                if (entered)
                    _bgSemaphore.Release();
                lock (_backgroundCancellationLock)
                {
                    if (ReferenceEquals(_backgroundLoadCts, loadCts))
                        _backgroundLoadCts = null;
                }
                loadCts.Dispose();
            }
        }

        private async Task LoadGameBackgroundCoreAsync(CancellationToken cancellationToken)
        {
            var main = _session.MainWindow;
            if (main == null) { Logger.Debug("main is null, skip", "BG"); return; }
            var profile = _session.Data.ActiveGame;
            if (profile == null) { Logger.Debug("profile is null, skip", "BG"); return; }
            var loadGameId = profile.Id;
            var loadGameBiz = _session.Data.ActiveGameBiz;
            bool IsStillActive() =>
                _session.Data.ActiveGameBiz == loadGameBiz &&
                _session.Data.ActiveGame?.Id == loadGameId;

            Logger.Debug("LoadGameBackground: game=" + profile.Id + " biz=" + loadGameBiz, "BG");

            // 1. Per-game custom background file
            string customBg = _session.Data.GetCustomBackground(profile.Id);
            if (!string.IsNullOrEmpty(customBg) && File.Exists(customBg))
            {
                Logger.Debug("Using custom background: " + customBg, "BG");
                if (!IsStillActive()) return;
                if (BackgroundService.IsVideoFile(customBg))
                {
                    var playable = await BackgroundService.PreparePlayableVideoAsync(customBg, profile.Id, customBg, cancellationToken);
                    if (!IsStillActive()) return;
                    System.Windows.Application.Current.Dispatcher.Invoke(() => main.SetBackgroundVideo(playable));
                }
                else
                    System.Windows.Application.Current.Dispatcher.Invoke(() => main.SetBackgroundImage(customBg));
                return;
            }

            // 2. Legacy global custom background (backward compat)
            string legacyBg = _session.Data.BackgroundPath;
            if (!string.IsNullOrEmpty(legacyBg) && File.Exists(legacyBg))
            {
                Logger.Debug("Using legacy background: " + legacyBg, "BG");
                if (!IsStillActive()) return;
                if (BackgroundService.IsVideoFile(legacyBg))
                {
                    var playable = await BackgroundService.PreparePlayableVideoAsync(legacyBg, profile.Id, legacyBg, cancellationToken);
                    if (!IsStillActive()) return;
                    System.Windows.Application.Current.Dispatcher.Invoke(() => main.SetBackgroundVideo(playable));
                }
                else
                    System.Windows.Application.Current.Dispatcher.Invoke(() => main.SetBackgroundImage(legacyBg));
                return;
            }

            // 3. API backgrounds
            Logger.Debug("Fetching API backgrounds...", "BG");
            var allBgs = await BackgroundService.FetchBackgroundsAsync(profile, loadGameBiz, cancellationToken);
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
            string preferredBackgroundId = _session.Data.GetSelectedBackgroundId(profile.Id);
            var orderedBackgrounds = allBgs
                .OrderByDescending(bg => !string.IsNullOrWhiteSpace(preferredBackgroundId) && bg.Id == preferredBackgroundId)
                .ToList();
            foreach (var bg in orderedBackgrounds)
            {
                string? url = bg.IsVideo ? bg.Video?.Url : bg.Background?.Url;
                if (string.IsNullOrEmpty(url) || url.StartsWith("pack:"))
                {
                    Logger.Debug("  skip bg id=" + bg.Id + " (no url)", "BG");
                    continue;
                }
                Logger.Debug("  trying bg id=" + bg.Id + " url=" + url, "BG");
                cacheFile = await BackgroundService.CacheBackgroundFileAsync(url, profile.Id, cancellationToken);
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
                    themePath = await BackgroundService.CacheBackgroundFileAsync(selected.Theme.Url, profile.Id, cancellationToken);
                if (!IsStillActive()) return;
                // Cache the static fallback image (for when video can't play, e.g. WebM)
                string? fallbackPath = null;
                if (selected.Background != null && !string.IsNullOrEmpty(selected.Background.Url))
                    fallbackPath = await BackgroundService.CacheBackgroundFileAsync(selected.Background.Url, profile.Id, cancellationToken);
                if (!IsStillActive()) return;
                string playbackPath = await BackgroundService.PreparePlayableVideoAsync(cacheFile, profile.Id, selected.Video?.Url, cancellationToken);
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
                _session.Data.SetSelectedBackgroundId(profile.Id, selected.Id);

            Logger.Debug("Background loaded OK: " + selected.Id, "BG");
        }

        public async Task<bool> CheckConfigAsync(MainWindow main)
        {
            if (!Directory.Exists("UserData"))
                Directory.CreateDirectory("UserData");

            // Snapshot configuration on the UI thread. Registry and directory
            // enumeration then runs in the background without touching the
            // non-thread-safe INI parser.
            var inputs = GameProfiles.All
                .SelectMany(profile => profile.GetSupportedServers().Select(biz => new GamePathScanInput(
                    profile,
                    biz,
                    _session.Data.GetExactGamePath(biz.Value),
                    _session.Data.GetGamePath(biz.Value))))
                .ToList();

            var repairs = await Task.Run(() => ScanGamePaths(inputs));
            bool changed = false;
            foreach (var repair in repairs)
            {
                // Do not overwrite a path the user changed while scanning.
                string current = _session.Data.GetExactGamePath(repair.Biz.Value);
                if (!PathsEqual(current, repair.OriginalPath))
                    continue;

                _session.Data.SetGamePath(repair.Biz.Value, repair.NewPath ?? string.Empty);
                Logger.Info(string.IsNullOrWhiteSpace(repair.NewPath)
                    ? $"Removed invalid cross-server path for {repair.Biz}: {repair.OriginalPath}"
                    : $"Auto-found game path: {repair.NewPath} ({repair.Biz})", "Main");
                changed = true;
            }
            return changed;
        }

        private static List<GamePathRepair> ScanGamePaths(IReadOnlyList<GamePathScanInput> inputs)
        {
            var repairs = new List<GamePathRepair>();
            foreach (var input in inputs)
            {
                if (!string.IsNullOrWhiteSpace(input.ExactPath) &&
                    GameStateService.IsGameInstalled(
                        input.ExactPath,
                        input.Profile,
                        input.Biz,
                        trustExplicitPath: true))
                    continue;

                // FindGame can carry a trusted per-biz registry hint even when
                // a same-name client has an incomplete config.ini.
                var foundPath = GameSearchService.FindGame(input.Profile, input.Biz.Server)?.Path;
                if (string.IsNullOrWhiteSpace(foundPath))
                {
                    var candidates = GameInstallService.GetKnownGamePathCandidates(
                        input.Profile,
                        input.Biz.Server,
                        new[] { input.ExactPath, input.FallbackPath });
                    foundPath = candidates.FirstOrDefault(path =>
                        GameStateService.IsGameInstalled(path, input.Profile, input.Biz));
                }
                if (!string.IsNullOrWhiteSpace(foundPath))
                {
                    if (!PathsEqual(foundPath, input.ExactPath))
                        repairs.Add(new GamePathRepair(input.Biz, input.ExactPath, foundPath));
                    continue;
                }

                // Clear an online path that visibly belongs to another server,
                // but retain unavailable external-drive paths.
                if (!string.IsNullOrWhiteSpace(input.ExactPath) &&
                    Directory.Exists(input.ExactPath) &&
                    IsConfirmedDifferentServer(input.ExactPath, input.Profile, input.Biz))
                    repairs.Add(new GamePathRepair(input.Biz, input.ExactPath, string.Empty));
            }
            return repairs;
        }

        private static bool IsConfirmedDifferentServer(string path, GameProfile profile, GameBiz expectedBiz)
        {
            var detectedServer = GameSearchService.DetectServerFromConfig(path);
            if (!string.IsNullOrWhiteSpace(detectedServer))
                return !string.Equals(detectedServer, expectedBiz.Server, StringComparison.OrdinalIgnoreCase);

            // When executable names differ they can prove CN/global.  A
            // same-name executable cannot prove a server and must not cause
            // an explicitly configured legacy path to be deleted.
            if (string.Equals(profile.CnExeName, profile.GlobalExeName, StringComparison.OrdinalIgnoreCase))
                return false;

            bool hasCnExe = File.Exists(Path.Combine(path, profile.CnExeName));
            bool hasGlobalExe = File.Exists(Path.Combine(path, profile.GlobalExeName));
            return expectedBiz.IsGlobalServer()
                ? hasCnExe && !hasGlobalExe
                : hasGlobalExe && !hasCnExe;
        }

        private static bool PathsEqual(string? left, string? right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
                return string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right);
            try
            {
                return string.Equals(
                    Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch { return string.Equals(left, right, StringComparison.OrdinalIgnoreCase); }
        }

        private sealed record GamePathScanInput(
            GameProfile Profile,
            GameBiz Biz,
            string? ExactPath,
            string? FallbackPath);

        private sealed record GamePathRepair(GameBiz Biz, string? OriginalPath, string? NewPath);
    }
}
