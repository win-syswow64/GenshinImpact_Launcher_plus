using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using GenShin_Launcher_Plus.Helper;
using GenShin_Launcher_Plus.Models;
using GenShin_Launcher_Plus.Models.HoYoPlay;
using LibVLCSharp.Shared;

namespace GenShin_Launcher_Plus.Service
{
    /// <summary>
    /// Manages per-game background images/videos.
    /// Uses the IP-selected HoYoPlay API domain and the launcher/game IDs for
    /// each game channel.
    /// </summary>
    public static class BackgroundService
    {
        private static readonly HttpClient _httpClient = new(new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        }) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

        private static readonly TimeSpan ApiRequestTimeout = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan BackgroundDownloadTimeout = TimeSpan.FromMinutes(2);
        private static readonly SemaphoreSlim VideoConversionSemaphore = new(1, 1);

        /// <summary>
        /// In-memory cache for API background responses, keyed by game type (e.g. "genshin").
        /// Avoids redundant API calls when switching between servers of the same game.
        /// </summary>
        private static readonly ConcurrentDictionary<string, (List<HoYoGameBackground> Backgrounds, DateTime Expiry)> _apiCache = new();
        private static readonly TimeSpan ApiCacheDuration = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Clear the API cache (call after manual refresh or when cache might be stale).
        /// </summary>
        public static void ClearApiCache() => _apiCache.Clear();

        private static string CacheFolder => Path.Combine(AppContext.BaseDirectory, "Config", "Backgrounds");
        private const string VideoCacheFolderName = "VideoCache";
        private static readonly TimeSpan VideoConvertTimeout = TimeSpan.FromMinutes(10);

        // ---- Server-aware API parameter mapping (from Starward) ----

        /// <summary>
        /// Get the correct HoYoPlay API game ID.
        /// Our GameBiz format: genshin_cn, starrail_global, zzz_bilibili, honkai3_cn
        /// HoYoPlay biz format: hk4e_cn, hkrpg_global, nap_bilibili, bh3_cn
        /// Source: Starward.Core HoYoPlay/GameId.cs
        /// </summary>
        private static string? GetApiGameId(string gameBiz)
        {
            return HoYoPlayGameMap.GetApiGameId(gameBiz);
        }

        /// <summary>
        /// Get the launcher ID for a GameBiz.
        /// Source: Starward.Core HoYoPlay/LauncherId.cs
        /// </summary>
        private static string? GetLauncherId(string gameBiz)
        {
            return HoYoPlayGameMap.GetLauncherId(gameBiz);
        }

        /// <summary>
        /// Resolve an artwork endpoint and the matching launcher/game ids.
        /// The host and ids form one regional set and must never be mixed.
        /// </summary>
        private static async ValueTask<(string GameBiz, string BaseUrl)> GetArtworkApiContextAsync(
            string gameBiz,
            System.Threading.CancellationToken cancellationToken)
        {
            var region = await ApiRegionService.GetCurrentRegionAsync(cancellationToken).ConfigureAwait(false);
            return (
                HoYoPlayGameMap.GetRegionalArtworkGameBiz(gameBiz, region.IsChina),
                HoYoPlayGameMap.GetRegionalArtworkApiBaseUrl(region.IsChina));
        }

        /// <summary>
        /// Build the full API URL for getAllGameBasicInfo.
        /// </summary>
        public static async ValueTask<string?> BuildApiUrlAsync(string gameBiz, System.Threading.CancellationToken cancellationToken = default)
        {
            var context = await GetArtworkApiContextAsync(gameBiz, cancellationToken).ConfigureAwait(false);
            var launcherId = GetLauncherId(context.GameBiz);
            var gameId = GetApiGameId(context.GameBiz);
            if (launcherId == null || gameId == null) return null;
            return $"{context.BaseUrl}getAllGameBasicInfo?launcher_id={launcherId}&language=zh-cn&game_id={gameId}";
        }

        /// <summary>
        /// Build the getGames API URL for fetching poster/display backgrounds.
        /// </summary>
        public static async ValueTask<string?> BuildGamesApiUrlAsync(string gameBiz, System.Threading.CancellationToken cancellationToken = default)
        {
            var context = await GetArtworkApiContextAsync(gameBiz, cancellationToken).ConfigureAwait(false);
            var launcherId = GetLauncherId(context.GameBiz);
            if (launcherId == null) return null;
            return $"{context.BaseUrl}getGames?launcher_id={launcherId}&language=zh-cn";
        }

        /// <summary>
        /// Fetch all available backgrounds for a game from the API.
        /// </summary>
        public static async Task<List<HoYoGameBackground>> FetchBackgroundsAsync(
            GameProfile profile,
            string gameBiz,
            System.Threading.CancellationToken cancellationToken = default)
        {
            var region = await ApiRegionService.GetCurrentRegionAsync(cancellationToken).ConfigureAwait(false);
            string artworkGameBiz = HoYoPlayGameMap.GetRegionalArtworkGameBiz(gameBiz, region.IsChina);
            string cacheKey = $"{profile.Id}:{(region.IsChina ? "cn" : "global")}";
            if (_apiCache.TryGetValue(cacheKey, out var cached) && cached.Expiry > DateTime.UtcNow)
            {
                Logger.Debug($"Background API cache hit for {cacheKey} (from {gameBiz})", "Background");
                return cached.Backgrounds;
            }

            using var cts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(12));
            var (_, backgrounds) = await FetchBackgroundsFromBizAsync(
                profile,
                artworkGameBiz,
                cts.Token,
                cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (backgrounds.Count > 0)
            {
                _apiCache[cacheKey] = (backgrounds, DateTime.UtcNow.Add(ApiCacheDuration));
                Logger.Debug($"Using background API {artworkGameBiz}; cached {backgrounds.Count} backgrounds for {cacheKey}", "Background");
                return backgrounds;
            }

            Logger.Warn($"No backgrounds found for {gameBiz}", "Background");
            return new List<HoYoGameBackground>();
        }

        private static async Task<(string Biz, List<HoYoGameBackground> Backgrounds)> FetchBackgroundsFromBizAsync(
            GameProfile profile,
            string gameBiz,
            System.Threading.CancellationToken ct,
            System.Threading.CancellationToken callerCancellationToken)
        {
            var result = new List<HoYoGameBackground>();
            var apiGameId = GetApiGameId(gameBiz);
            if (apiGameId == null)
            {
                Logger.Warn($"Unknown game biz: {gameBiz}", "Background");
                return (gameBiz, result);
            }

            try
            {
                string? url = await BuildApiUrlAsync(gameBiz, ct).ConfigureAwait(false);
                if (url == null) return (gameBiz, result);

                Logger.Debug($"Fetching backgrounds: {url}", "Background");
                var response = await GetStringWithUserAgentAsync(url, ct);
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                if (root.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("game_info_list", out var list))
                {
                    for (int i = 0; i < list.GetArrayLength(); i++)
                    {
                        var item = list[i];
                        if (item.TryGetProperty("game", out var game) &&
                            game.TryGetProperty("id", out var id) &&
                            id.GetString() == apiGameId &&
                            item.TryGetProperty("backgrounds", out var bgs))
                        {
                            for (int j = 0; j < bgs.GetArrayLength(); j++)
                            {
                                try
                                {
                                    var bg = JsonSerializer.Deserialize<HoYoGameBackground>(
                                        bgs[j].GetRawText());
                                    if (bg?.Background?.Url != null)
                                    {
                                        Logger.Debug($"Background[{j}]: id={bg.Id} type={bg.Type} bg={bg.Background?.Url}", "Background");
                                        result.Add(bg);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Logger.Warn($"Parse background[{j}] failed: {ex.Message}", "Background");
                                }
                            }
                            break;
                        }
                    }
                }

                // Also get poster from getGames API
                try
                {
                    string? infoUrl = await BuildGamesApiUrlAsync(gameBiz, ct).ConfigureAwait(false);
                    if (infoUrl != null)
                    {
                        var infoResponse = await GetStringWithUserAgentAsync(infoUrl, ct);
                        using var infoDoc = JsonDocument.Parse(infoResponse);
                        var infoRoot = infoDoc.RootElement;
                        if (infoRoot.TryGetProperty("data", out var infoData) &&
                            infoData.TryGetProperty("games", out var games))
                        {
                            for (int i = 0; i < games.GetArrayLength(); i++)
                            {
                                var g = games[i];
                                if (g.TryGetProperty("id", out var gid) && gid.GetString() == apiGameId)
                                {
                                    if (g.TryGetProperty("display", out var display) &&
                                        display.TryGetProperty("background", out var posterBg) &&
                                        posterBg.TryGetProperty("url", out var posterUrl))
                                    {
                                        var urlStr = posterUrl.GetString();
                                        if (!string.IsNullOrEmpty(urlStr))
                                        {
                                            Logger.Debug($"Poster: {urlStr}", "Background");
                                            result.Add(new HoYoGameBackground
                                            {
                                                Id = $"poster_{profile.Id}",
                                                Background = new HoYoImage { Url = urlStr },
                                                Type = "BACKGROUND_TYPE_UNSPECIFIED",
                                            });
                                        }
                                    }
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (OperationCanceledException) when (callerCancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Failed to fetch poster: {ex.Message}", "Background");
                }

                if (result.Count == 0)
                {
                    Logger.Warn($"No backgrounds found for {gameBiz}", "Background");
                }
            }
            catch (OperationCanceledException) when (callerCancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to fetch backgrounds: {ex.Message}", "Background");
            }

            return (gameBiz, result);
        }

        private static async Task<string> GetStringWithUserAgentAsync(string url, System.Threading.CancellationToken ct)
        {
            using var timeout = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ApiRequestTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Overload that infers gameBiz from the active DataModel.
        /// </summary>
        public static Task<List<HoYoGameBackground>> FetchBackgroundsAsync(GameProfile profile)
        {
            string gameBiz = App.Current.DataModel.ActiveGameBiz;
            return FetchBackgroundsAsync(profile, gameBiz);
        }

        /// <summary>
        /// Download and cache a background file (image or video). Returns local file path.
        /// Validates the download is not empty and uses the correct extension.
        /// </summary>
        public static async Task<string?> CacheBackgroundFileAsync(
            string url,
            string gameId,
            System.Threading.CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(url)) return null;
            string folder = Path.Combine(CacheFolder, gameId);
            Directory.CreateDirectory(folder);

            string fileName = GetFileNameFromUrl(url);
            string filePath = Path.Combine(folder, fileName);
            string metaPath = GetSourceMetaPath(filePath);

            if (File.Exists(filePath) && new FileInfo(filePath).Length > 1024)
            {
                var metadata = ReadJsonFile<BackgroundFileMetadata>(metaPath);
                if (metadata != null && string.Equals(metadata.Url, url, StringComparison.Ordinal))
                {
                    Logger.Debug("Cache hit: " + filePath, "BG");
                    return filePath;
                }

                Logger.Debug(metadata == null
                    ? "Cache metadata missing, refreshing: " + filePath
                    : "Cache URL changed, refreshing: " + filePath, "BG");
                DeleteFileQuietly(filePath);
                DeleteFileQuietly(metaPath);
                DeleteConvertedVideosForSource(folder, filePath);
            }

            DeleteFileQuietly(filePath);
            DeleteFileQuietly(metaPath);

            try
            {
                Logger.Debug("Downloading: " + url, "Background");
                using var timeout = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(BackgroundDownloadTimeout);
                var bytes = await _httpClient.GetByteArrayAsync(url, timeout.Token).ConfigureAwait(false);
                if (bytes.Length < 1024)
                {
                    Logger.Warn("Downloaded file too small (" + bytes.Length + " bytes), skipping", "Background");
                    return null;
                }

                // Detect actual format from magic bytes and fix extension if needed
                string ext = DetectImageFormat(bytes, fileName);
                if (ext != Path.GetExtension(fileName))
                {
                    fileName = Path.GetFileNameWithoutExtension(fileName) + ext;
                    filePath = Path.Combine(folder, fileName);
                    metaPath = GetSourceMetaPath(filePath);
                    DeleteFileQuietly(filePath);
                    DeleteFileQuietly(metaPath);
                }

                await File.WriteAllBytesAsync(filePath, bytes, cancellationToken).ConfigureAwait(false);
                WriteSourceMetadata(metaPath, url);
                Logger.Debug("Cached: " + filePath + " (" + bytes.Length + " bytes)", "Background");

                return filePath;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Warn("Failed to download background: " + ex.Message, "Background");
                return null;
            }
        }

        public static async Task<string> PreparePlayableVideoAsync(
            string sourcePath,
            string gameId,
            string? sourceUrl = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                return sourcePath;

            var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (ext == ".mp4")
                return sourcePath;
            if (ext is not ".webm" and not ".mkv")
                return sourcePath;

            var sourceStamp = VideoSourceStamp.FromFile(sourcePath, sourceUrl);
            if (sourceStamp == null)
                return sourcePath;

            string cacheFolder = GetVideoCacheFolder(gameId);
            Directory.CreateDirectory(cacheFolder);

            string cacheKey = BuildVideoCacheKey(sourceStamp);
            string mp4Path = Path.Combine(cacheFolder, cacheKey + ".mp4");
            string metaPath = GetConvertedVideoMetaPath(mp4Path);

            if (IsConvertedVideoFresh(mp4Path, metaPath, sourceStamp))
            {
                Logger.Debug("MP4 video cache hit: " + mp4Path, "BG");
                return mp4Path;
            }

            await VideoConversionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Another caller may have completed this conversion while we
                // waited for the shared converter.
                if (IsConvertedVideoFresh(mp4Path, metaPath, sourceStamp))
                    return mp4Path;

                CleanupConvertedVideoCache(cacheFolder, mp4Path);
                DeleteFileQuietly(mp4Path);
                DeleteFileQuietly(metaPath);

                Logger.Debug("Converting background video to MP4: " + sourcePath, "BG");
                bool converted = await TryConvertVideoToMp4Async(sourcePath, mp4Path, cancellationToken).ConfigureAwait(false);
                if (!converted)
                {
                    Logger.Warn("MP4 conversion failed, using original video: " + sourcePath, "BG");
                    return sourcePath;
                }

                WriteJsonFile(metaPath, ConvertedVideoMetadata.FromStamp(sourceStamp));
                Logger.Debug("MP4 video cache ready: " + mp4Path, "BG");
                return mp4Path;
            }
            finally
            {
                VideoConversionSemaphore.Release();
            }
        }

        public static void CleanupUnusedBackgroundCache(string gameId, IEnumerable<string?> keepFiles)
        {
            string folder = Path.Combine(CacheFolder, gameId);
            if (!Directory.Exists(folder)) return;

            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in keepFiles)
            {
                if (string.IsNullOrWhiteSpace(file)) continue;
                try
                {
                    string full = Path.GetFullPath(file);
                    keep.Add(full);
                    keep.Add(GetSourceMetaPath(full));
                    keep.Add(GetConvertedVideoMetaPath(full));
                }
                catch { }
            }

            try
            {
                foreach (var file in Directory.GetFiles(folder))
                {
                    string full = Path.GetFullPath(file);
                    if (keep.Contains(full)) continue;
                    DeleteFileQuietly(full);
                    Logger.Debug("Removed stale background cache: " + full, "BG");
                }
            }
            catch { }

            CleanupConvertedVideoCache(GetVideoCacheFolder(gameId), keep);
        }

        private static async Task<bool> TryConvertVideoToMp4Async(
            string sourcePath,
            string mp4Path,
            CancellationToken cancellationToken)
        {
            string? dir = Path.GetDirectoryName(mp4Path);
            if (string.IsNullOrEmpty(dir)) return false;
            Directory.CreateDirectory(dir);

            string tempPath = Path.Combine(dir, Path.GetFileNameWithoutExtension(mp4Path) + ".tmp.mp4");
            DeleteFileQuietly(tempPath);

            if (await TryConvertWithFfmpegAsync(sourcePath, tempPath, cancellationToken).ConfigureAwait(false))
            {
                ReplaceFile(tempPath, mp4Path);
                return IsUsableMediaFile(mp4Path);
            }

            DeleteFileQuietly(tempPath);
            cancellationToken.ThrowIfCancellationRequested();
            if (await TryConvertWithLibVlcAsync(sourcePath, tempPath, cancellationToken).ConfigureAwait(false))
            {
                ReplaceFile(tempPath, mp4Path);
                return IsUsableMediaFile(mp4Path);
            }

            DeleteFileQuietly(tempPath);
            return false;
        }

        private static async Task<bool> TryConvertWithFfmpegAsync(
            string sourcePath,
            string tempPath,
            CancellationToken cancellationToken)
        {
            string? ffmpeg = FindFfmpegExecutable();
            if (ffmpeg == null) return false;

            try
            {
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpeg,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                };
                process.StartInfo.ArgumentList.Add("-y");
                process.StartInfo.ArgumentList.Add("-hide_banner");
                process.StartInfo.ArgumentList.Add("-loglevel");
                process.StartInfo.ArgumentList.Add("error");
                process.StartInfo.ArgumentList.Add("-i");
                process.StartInfo.ArgumentList.Add(sourcePath);
                process.StartInfo.ArgumentList.Add("-an");
                process.StartInfo.ArgumentList.Add("-c:v");
                process.StartInfo.ArgumentList.Add("libx264");
                process.StartInfo.ArgumentList.Add("-preset");
                process.StartInfo.ArgumentList.Add("veryfast");
                process.StartInfo.ArgumentList.Add("-crf");
                process.StartInfo.ArgumentList.Add("23");
                process.StartInfo.ArgumentList.Add("-pix_fmt");
                process.StartInfo.ArgumentList.Add("yuv420p");
                process.StartInfo.ArgumentList.Add("-movflags");
                process.StartInfo.ArgumentList.Add("+faststart");
                process.StartInfo.ArgumentList.Add(tempPath);

                if (!process.Start())
                    return false;

                var stdOutTask = process.StandardOutput.ReadToEndAsync();
                var stdErrTask = process.StandardError.ReadToEndAsync();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(VideoConvertTimeout);
                try
                {
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    TryKillProcess(process);
                    throw;
                }
                catch (OperationCanceledException)
                {
                    TryKillProcess(process);
                    Logger.Warn("ffmpeg conversion timeout", "BG");
                    return false;
                }

                string stderr = await stdErrTask.ConfigureAwait(false);
                _ = await stdOutTask.ConfigureAwait(false);
                if (process.ExitCode != 0)
                {
                    Logger.Warn("ffmpeg conversion failed: " + stderr.Trim(), "BG");
                    return false;
                }

                return IsUsableMediaFile(tempPath);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Warn("ffmpeg conversion exception: " + ex.Message, "BG");
                return false;
            }
        }

        private static async Task<bool> TryConvertWithLibVlcAsync(
            string sourcePath,
            string tempPath,
            CancellationToken cancellationToken)
        {
            try
            {
                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using var libVLC = new LibVLC("--quiet", "--no-video-title-show", "--no-stats");
                using var media = new Media(libVLC, new Uri(sourcePath));
                media.AddOption(":no-audio");
                media.AddOption(":sout=#transcode{vcodec=h264,vb=6000,acodec=none}:std{access=file,mux=mp4,dst='" + EscapeVlcSoutPath(tempPath) + "'}");
                media.AddOption(":no-sout-all");

                using var player = new LibVLCSharp.Shared.MediaPlayer(libVLC);
                EventHandler<EventArgs> ended = (_, _) => tcs.TrySetResult(true);
                EventHandler<EventArgs> error = (_, _) => tcs.TrySetResult(false);
                player.EndReached += ended;
                player.EncounteredError += error;

                try
                {
                    if (!player.Play(media))
                        return false;

                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(VideoConvertTimeout);
                    try
                    {
                        bool converted = await tcs.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                        return converted && IsUsableMediaFile(tempPath);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (OperationCanceledException)
                    {
                        Logger.Warn("LibVLC conversion timeout", "BG");
                        return false;
                    }
                }
                finally
                {
                    player.EndReached -= ended;
                    player.EncounteredError -= error;
                    try { player.Stop(); } catch { }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Warn("LibVLC conversion exception: " + ex.Message, "BG");
                return false;
            }
        }

        private static string? FindFfmpegExecutable()
        {
            string[] localCandidates =
            {
                Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe"),
                Path.Combine(AppContext.BaseDirectory, "Tools", "ffmpeg.exe"),
                Path.Combine(AppContext.BaseDirectory, "Bin", "ffmpeg.exe"),
            };
            foreach (var candidate in localCandidates)
                if (File.Exists(candidate)) return candidate;

            string? path = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrWhiteSpace(path)) return null;

            foreach (var dir in path.Split(Path.PathSeparator))
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(dir)) continue;
                    string candidate = Path.Combine(dir.Trim(), "ffmpeg.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }

            return null;
        }

        private static bool IsConvertedVideoFresh(string mp4Path, string metaPath, VideoSourceStamp sourceStamp)
        {
            if (!IsUsableMediaFile(mp4Path)) return false;
            var metadata = ReadJsonFile<ConvertedVideoMetadata>(metaPath);
            return metadata != null && metadata.Matches(sourceStamp);
        }

        private static bool IsUsableMediaFile(string path)
        {
            try { return File.Exists(path) && new FileInfo(path).Length > 1024 * 64; }
            catch { return false; }
        }

        private static string BuildVideoCacheKey(VideoSourceStamp stamp)
        {
            string raw = string.Join("|", stamp.SourcePath, stamp.SourceUrl, stamp.Length, stamp.LastWriteUtcTicks);
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static string GetVideoCacheFolder(string gameId)
        {
            return Path.Combine(CacheFolder, gameId, VideoCacheFolderName);
        }

        private static string GetSourceMetaPath(string filePath)
        {
            return filePath + ".meta.json";
        }

        private static string GetConvertedVideoMetaPath(string mp4Path)
        {
            return mp4Path + ".json";
        }

        private static void DeleteConvertedVideosForSource(string backgroundFolder, string sourcePath)
        {
            string videoCacheFolder = Path.Combine(backgroundFolder, VideoCacheFolderName);
            if (!Directory.Exists(videoCacheFolder)) return;
            string normalizedSource = NormalizePath(sourcePath);

            try
            {
                foreach (var metaPath in Directory.GetFiles(videoCacheFolder, "*.mp4.json"))
                {
                    var metadata = ReadJsonFile<ConvertedVideoMetadata>(metaPath);
                    if (metadata == null || !string.Equals(metadata.SourcePath, normalizedSource, StringComparison.OrdinalIgnoreCase))
                        continue;
                    string mp4Path = metaPath[..^5];
                    DeleteFileQuietly(mp4Path);
                    DeleteFileQuietly(metaPath);
                }
            }
            catch { }
        }

        private static void CleanupConvertedVideoCache(string videoCacheFolder, string? keepMp4Path)
        {
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(keepMp4Path))
            {
                string full = Path.GetFullPath(keepMp4Path);
                keep.Add(full);
                keep.Add(GetConvertedVideoMetaPath(full));
            }
            CleanupConvertedVideoCache(videoCacheFolder, keep);
        }

        private static void CleanupConvertedVideoCache(string videoCacheFolder, HashSet<string> keep)
        {
            if (!Directory.Exists(videoCacheFolder)) return;
            try
            {
                foreach (var file in Directory.GetFiles(videoCacheFolder))
                {
                    string full = Path.GetFullPath(file);
                    if (keep.Contains(full)) continue;
                    DeleteFileQuietly(full);
                    Logger.Debug("Removed stale MP4 cache: " + full, "BG");
                }
            }
            catch { }
        }

        private static void WriteSourceMetadata(string metaPath, string url)
        {
            WriteJsonFile(metaPath, new BackgroundFileMetadata
            {
                Url = url,
                CachedAtUtc = DateTime.UtcNow,
            });
        }

        private static T? ReadJsonFile<T>(string path)
        {
            try
            {
                if (!File.Exists(path)) return default;
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
            }
            catch { return default; }
        }

        private static void WriteJsonFile<T>(string path, T value)
        {
            try
            {
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, JsonSerializer.Serialize(value));
            }
            catch { }
        }

        private static void ReplaceFile(string sourcePath, string targetPath)
        {
            DeleteFileQuietly(targetPath);
            File.Move(sourcePath, targetPath);
        }

        private static void DeleteFileQuietly(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch { }
        }

        private static void TryKillProcess(Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch { }
        }

        private static string EscapeVlcSoutPath(string path)
        {
            return Path.GetFullPath(path).Replace('\\', '/').Replace("'", "\\'");
        }

        private static string NormalizePath(string path)
        {
            try { return Path.GetFullPath(path); }
            catch { return path; }
        }

        /// <summary>
        /// Detect image/video format from magic bytes. Falls back to the original filename extension.
        /// </summary>
        private static string DetectImageFormat(byte[] data, string originalName)
        {
            if (data.Length >= 4)
            {
                // JPEG: FF D8 FF
                if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return ".jpg";
                // PNG: 89 50 4E 47
                if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) return ".png";
                // BMP: 42 4D
                if (data[0] == 0x42 && data[1] == 0x4D) return ".bmp";
                // GIF: 47 49 46
                if (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46) return ".gif";
                // RIFF (WebP): 52 49 46 46
                if (data.Length >= 12 && data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46
                    && data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50) return ".webp";
                // WebM/Matroska: EBML header 1A 45 DF A3
                if (data[0] == 0x1A && data[1] == 0x45 && data[2] == 0xDF && data[3] == 0xA3) return ".webm";
                // ftyp (MP4): at offset 4
                if (data.Length >= 8 && data[4] == 0x66 && data[5] == 0x74 && data[6] == 0x79 && data[7] == 0x70) return ".mp4";
            }
            // Fallback to the original name extension
            string ext = Path.GetExtension(originalName);
            return string.IsNullOrEmpty(ext) ? ".jpg" : ext;
        }

        /// <summary>
        /// Get the suggested background for a game based on last selection.
        /// </summary>
        public static async Task<HoYoGameBackground?> GetSuggestedBackgroundAsync(GameProfile profile)
        {
            // Check for custom background file first
            string customPath = App.Current.DataModel.GetCustomBackground(profile.Id);
            if (!string.IsNullOrEmpty(customPath) && File.Exists(customPath))
            {
                return HoYoGameBackground.Custom(customPath);
            }

            var backgrounds = await FetchBackgroundsAsync(profile);
            if (backgrounds.Count == 0) return null;

            string selectedId = App.Current.DataModel.GetSelectedBackgroundId(profile.Id);

            // Try to match the last selected background
            HoYoGameBackground? selected = null;
            if (!string.IsNullOrEmpty(selectedId))
            {
                selected = backgrounds.FirstOrDefault(b => b.Id == selectedId);
            }

            selected ??= backgrounds.FirstOrDefault();

            // Cache the file
            if (selected != null && !selected.IsCustom)
            {
                string? url = selected.IsVideo ? selected.Video?.Url : selected.Background?.Url;
                if (!string.IsNullOrEmpty(url))
                {
                    string? cached = await CacheBackgroundFileAsync(url, profile.Id);
                    selected.CacheFile = cached;
                }
            }

            return selected;
        }

        /// <summary>
        /// Check if a file is a supported video format.
        /// </summary>
        public static bool IsVideoFile(string? path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return Path.GetExtension(path)?.ToLower() switch
            {
                ".mp4" or ".mkv" or ".webm" => true,
                _ => false,
            };
        }

        /// <summary>
        /// Per-game background folder path.
        /// </summary>
        public static string GetGameCacheFolder(string gameId)
        {
            return Path.Combine(CacheFolder, gameId);
        }

        private static string GetFileNameFromUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                // Remove query string from the filename
                string name = Path.GetFileName(uri.LocalPath);
                if (string.IsNullOrEmpty(name) || !name.Contains('.'))
                    name = Guid.NewGuid().ToString("N") + ".jpg";
                return name;
            }
            catch
            {
                return Guid.NewGuid().ToString("N") + ".jpg";
            }
        }

        private sealed class BackgroundFileMetadata
        {
            [JsonPropertyName("url")]
            public string Url { get; set; } = "";

            [JsonPropertyName("cached_at_utc")]
            public DateTime CachedAtUtc { get; set; }
        }

        private sealed class VideoSourceStamp
        {
            public string SourcePath { get; init; } = "";
            public string SourceUrl { get; init; } = "";
            public long Length { get; init; }
            public long LastWriteUtcTicks { get; init; }

            public static VideoSourceStamp? FromFile(string sourcePath, string? sourceUrl)
            {
                try
                {
                    var file = new FileInfo(sourcePath);
                    if (!file.Exists || file.Length <= 0) return null;
                    return new VideoSourceStamp
                    {
                        SourcePath = NormalizePath(sourcePath),
                        SourceUrl = sourceUrl ?? "",
                        Length = file.Length,
                        LastWriteUtcTicks = file.LastWriteTimeUtc.Ticks,
                    };
                }
                catch { return null; }
            }
        }

        private sealed class ConvertedVideoMetadata
        {
            [JsonPropertyName("source_path")]
            public string SourcePath { get; set; } = "";

            [JsonPropertyName("source_url")]
            public string SourceUrl { get; set; } = "";

            [JsonPropertyName("source_length")]
            public long SourceLength { get; set; }

            [JsonPropertyName("source_last_write_utc_ticks")]
            public long SourceLastWriteUtcTicks { get; set; }

            [JsonPropertyName("converted_at_utc")]
            public DateTime ConvertedAtUtc { get; set; }

            public static ConvertedVideoMetadata FromStamp(VideoSourceStamp stamp)
            {
                return new ConvertedVideoMetadata
                {
                    SourcePath = stamp.SourcePath,
                    SourceUrl = stamp.SourceUrl,
                    SourceLength = stamp.Length,
                    SourceLastWriteUtcTicks = stamp.LastWriteUtcTicks,
                    ConvertedAtUtc = DateTime.UtcNow,
                };
            }

            public bool Matches(VideoSourceStamp stamp)
            {
                return string.Equals(SourcePath, stamp.SourcePath, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(SourceUrl, stamp.SourceUrl, StringComparison.Ordinal)
                    && SourceLength == stamp.Length
                    && SourceLastWriteUtcTicks == stamp.LastWriteUtcTicks;
            }
        }
    }
}
