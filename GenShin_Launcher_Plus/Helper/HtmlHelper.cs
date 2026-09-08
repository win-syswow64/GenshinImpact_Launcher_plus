using System;
using System.Net;
using System.Net.Http;
using GenShin_Launcher_Plus.Helper;
using GenShin_Launcher_Plus.Service;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GenShin_Launcher_Plus.Helper
{
    public static class HtmlHelper
    {
        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(15),
        };

        public static async Task<JsonElement> GetAPIData(string url, CancellationToken cancellationToken = default)
        {
            using var response = await HttpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var doc = JsonDocument.Parse(responseBody);
            return doc.RootElement.Clone();
        }

        public static async Task<string> GetInfoFromHtmlAsync(string tag)
        {
            const string url = "https://api.nahidaya.top/API/genshinimpact.php";
            try
            {
                var data = await GetAPIData(url).ConfigureAwait(false);
                var result = data.GetProperty(tag).ToString();
                Logger.Debug($"Info from API ({tag}): {result}", "API");
                return result;
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to get info ({tag}): {ex.Message}", "API");
                return string.Empty;
            }
        }

        /// <summary>
        /// Follow the 302 redirect from the daily image API and return the direct image URL.
        /// </summary>
        public static async Task<string> GetDailyImageDirectUrlAsync()
        {
            const string url = "https://api.nahidaya.top/API/score.php";
            try
            {
                using var handler = new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    AutomaticDecompression = DecompressionMethods.All
                };
                using var client = new HttpClient(handler);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                client.Timeout = TimeSpan.FromSeconds(15);

                var response = await client.GetAsync(url).ConfigureAwait(false);
                var location = response.Headers.Location;
                if (location != null)
                {
                    return location.IsAbsoluteUri
                        ? location.AbsoluteUri
                        : location.ToString();
                }
                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        public static async Task<string> GetBackgroundImageUrlAsync()
        {
            var profile = App.Current.DataModel?.ActiveGame;
            if (profile == null) return string.Empty;
            string gameBiz = App.Current.DataModel.ActiveGameBiz;
            try
            {
                // Keep all artwork routing in BackgroundService. It chooses
                // the API host and its matching launcher/game ids by IP.
                var backgrounds = await BackgroundService.FetchBackgroundsAsync(profile, gameBiz).ConfigureAwait(false);
                foreach (var background in backgrounds)
                {
                    if (!string.IsNullOrWhiteSpace(background.Background?.Url))
                        return background.Background.Url;
                }
                return string.Empty;
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to get background image: {ex.Message}", "API");
                return string.Empty;
            }
        }

        public static async Task<string> GetPkgVersionAsync()
        {
            var profile = App.Current.DataModel?.ActiveGame;
            if (profile == null) return string.Empty;
            string gameBiz = App.Current.DataModel.ActiveGameBiz;
            try
            {
                var (version, _) = await HoYoPlayApiService.GetLatestVersionsAsync(gameBiz).ConfigureAwait(false);
                Logger.Debug($"PKG version for {profile.Id}: {version}", "API");
                return version ?? string.Empty;
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to get PKG version: {ex.Message}", "API");
                return string.Empty;
            }
        }
    }
}
