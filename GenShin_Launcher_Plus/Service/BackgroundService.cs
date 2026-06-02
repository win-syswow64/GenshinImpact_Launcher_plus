using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using GenShin_Launcher_Plus.Helper;
using GenShin_Launcher_Plus.Models;

namespace GenShin_Launcher_Plus.Service
{
    /// <summary>
    /// Manages per-game background images/videos.
    /// Uses correct HoYoPlay API domain, launcher ID, and game ID per server.
    /// </summary>
    public static class BackgroundService
    {
        private static readonly HttpClient _httpClient = new(new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All
        });

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

        // ---- Server-aware API parameter mapping (from Starward) ----

        /// <summary>
        /// Get the correct HoYoPlay API game ID.
        /// Our GameBiz format: genshin_cn, starrail_global, zzz_bilibili, honkai3_cn
        /// HoYoPlay biz format: hk4e_cn, hkrpg_global, nap_bilibili, bh3_cn
        /// Source: Starward.Core HoYoPlay/GameId.cs
        /// </summary>
        private static string? GetApiGameId(string gameBiz)
        {
            return gameBiz switch
            {
                "genshin_cn" => "1Z8W5NHUQb",
                "genshin_global" => "gopR6Cufr3",
                "genshin_bilibili" => "T2S0Gz4Dr2",
                "starrail_cn" => "64kMb5iAWu",
                "starrail_global" => "4ziysqXOQ8",
                "starrail_bilibili" => "EdtUqXfCHh",
                "zzz_cn" => "x6znKlJ0xK",
                "zzz_global" => "U5hbdsT9W7",
                "zzz_bilibili" => "HXAFlmYa17",
                "honkai3_cn" => "osvnlOc0S8",
                "honkai3_global" => "5TIVvvcwtM",
                _ => null,
            };
        }

        /// <summary>
        /// Get the launcher ID for a GameBiz.
        /// Source: Starward.Core HoYoPlay/LauncherId.cs
        /// </summary>
        private static string? GetLauncherId(string gameBiz)
        {
            return gameBiz switch
            {
                "genshin_cn" or "starrail_cn" or "honkai3_cn" or "zzz_cn" => "jGHBHlcOq1",
                "genshin_global" or "starrail_global" or "honkai3_global" or "zzz_global" => "VYTpXlbWo8",
                "genshin_bilibili" => "umfgRO5gh5",
                "starrail_bilibili" => "6P5gHMNyK3",
                "zzz_bilibili" => "xV0f4r1GT0",
                _ => null,
            };
        }

        /// <summary>
        /// Get the API base URL for a GameBiz.
        /// CN uses mihoyo.com, Global uses hoyoverse.com.
        /// </summary>
        private static string GetApiBaseUrl(string gameBiz)
        {
            // Global servers use hoyoverse.com, CN/bilibili use mihoyo.com
            if (gameBiz.Contains("_global"))
                return "https://sg-hyp-api.hoyoverse.com/hyp/hyp-connect/api";
            return "https://hyp-api.mihoyo.com/hyp/hyp-connect/api";
        }

        /// <summary>
        /// Build the full API URL for getAllGameBasicInfo.
        /// </summary>
        public static string? BuildApiUrl(string gameBiz)
        {
            var launcherId = GetLauncherId(gameBiz);
            var gameId = GetApiGameId(gameBiz);
            if (launcherId == null || gameId == null) return null;
            var baseUrl = GetApiBaseUrl(gameBiz);
            return $"{baseUrl}/getAllGameBasicInfo?launcher_id={launcherId}&language=zh-cn&game_id={gameId}";
        }

        /// <summary>
        /// Build the getGames API URL for fetching poster/display backgrounds.
        /// </summary>
        public static string? BuildGamesApiUrl(string gameBiz)
        {
            var launcherId = GetLauncherId(gameBiz);
            if (launcherId == null) return null;
            var baseUrl = GetApiBaseUrl(gameBiz);
            return $"{baseUrl}/getGames?launcher_id={launcherId}&language=zh-cn";
        }

        /// <summary>
        /// Fetch all available backgrounds for a game from the API.
        /// </summary>
        public static async Task<List<HoYoGameBackground>> FetchBackgroundsAsync(GameProfile profile, string gameBiz)
        {
            var result = new List<HoYoGameBackground>();
            var apiGameId = GetApiGameId(gameBiz);
            if (apiGameId == null)
            {
                Logger.Warn($"Unknown game biz: {gameBiz}", "Background");
                return result;
            }

            // Check in-memory cache: backgrounds are the same across all servers of a game
            string gameType = profile.Id; // e.g. "genshin", "starrail"
            if (_apiCache.TryGetValue(gameType, out var cached) && cached.Expiry > DateTime.UtcNow)
            {
                Logger.Debug($"Background API cache hit for {gameType} (from {gameBiz})", "Background");
                return cached.Backgrounds;
            }

            try
            {
                string? url = BuildApiUrl(gameBiz);
                if (url == null) return result;

                _httpClient.DefaultRequestHeaders.UserAgent.Clear();
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

                Logger.Debug($"Fetching backgrounds: {url}", "Background");
                var response = await _httpClient.GetStringAsync(url);
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
                    string? infoUrl = BuildGamesApiUrl(gameBiz);
                    if (infoUrl != null)
                    {
                        var infoResponse = await _httpClient.GetStringAsync(infoUrl);
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
                catch (Exception ex)
                {
                    Logger.Warn($"Failed to fetch poster: {ex.Message}", "Background");
                }

                if (result.Count == 0)
                {
                    Logger.Warn($"No backgrounds found for {gameBiz}", "Background");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to fetch backgrounds: {ex.Message}", "Background");
            }

            // Cache the result for this game type (shared across servers)
            if (result.Count > 0)
            {
                _apiCache[gameType] = (result, DateTime.UtcNow.Add(ApiCacheDuration));
                Logger.Debug($"Cached {result.Count} backgrounds for {gameType}", "Background");
            }

            return result;
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
        public static async Task<string?> CacheBackgroundFileAsync(string url, string gameId)
        {
            if (string.IsNullOrEmpty(url)) return null;
            string folder = Path.Combine(CacheFolder, gameId);
            Directory.CreateDirectory(folder);

            string fileName = GetFileNameFromUrl(url);
            string filePath = Path.Combine(folder, fileName);

            // If the exact filename already exists locally and is valid, skip download
            if (File.Exists(filePath) && new FileInfo(filePath).Length > 1024)
            {
                Logger.Debug("Cache hit (filename match): " + filePath, "BG");
                return filePath;
            }

            // Filename mismatch: clean up only the stale file (partial download or old cache)
            // before downloading the new one. Other valid cached files are left intact.
            try { File.Delete(filePath); } catch { }

            try
            {
                Logger.Debug("Downloading: " + url, "Background");
                var bytes = await _httpClient.GetByteArrayAsync(url);
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
                }

                await File.WriteAllBytesAsync(filePath, bytes);
                Logger.Debug("Cached: " + filePath + " (" + bytes.Length + " bytes)", "Background");

                // Remove other stale files in this game's cache folder
                try
                {
                    foreach (var old in Directory.GetFiles(folder))
                    {
                        if (!string.Equals(old, filePath, StringComparison.OrdinalIgnoreCase))
                        {
                            try { File.Delete(old); Logger.Debug("Removed stale cache: " + old, "BG"); }
                            catch { }
                        }
                    }
                }
                catch { }

                return filePath;
            }
            catch (Exception ex)
            {
                Logger.Warn("Failed to download background: " + ex.Message, "Background");
                return null;
            }
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
                if (data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46) return ".webp";
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
    }
}