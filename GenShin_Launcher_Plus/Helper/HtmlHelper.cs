using System;
using System.Net;
using System.Net.Http;
using GenShin_Launcher_Plus.Helper;
using System.Text.Json;
using System.Threading.Tasks;

namespace GenShin_Launcher_Plus.Helper
{
    public static class HtmlHelper
    {
        public static async Task<JsonElement> GetAPIData(string url)
        {
            using var client = new HttpClient();
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            string responseBody = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(responseBody);
            return doc.RootElement.Clone();
        }

        public static async Task<string> GetInfoFromHtmlAsync(string tag)
        {
            const string url = "https://api.nahidaya.top/API/genshinimpact.php";
            try
            {
                var data = await GetAPIData(url);
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

                var response = await client.GetAsync(url);
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
            string url = $"https://hyp-api.mihoyo.com/hyp/hyp-connect/api/getAllGameBasicInfo?launcher_id={profile.ApiLauncherId}&language=zh-cn";
            try
            {
                var data = await GetAPIData(url);
                var list = data.GetProperty("data").GetProperty("game_info_list");
                for (int i = 0; i < list.GetArrayLength(); i++)
                {
                    var gameId = list[i].GetProperty("game").GetProperty("id").ToString();
                    if (gameId == profile.ApiGameId)
                    {
                        var bgUrl = list[i].GetProperty("backgrounds")[0]
                            .GetProperty("background")
                            .GetProperty("url")
                            .ToString();
                        Logger.Debug($"Background URL for {profile.Id}: {bgUrl}", "API");
                        return bgUrl;
                    }
                }
                var fallback = list[0].GetProperty("backgrounds")[0]
                    .GetProperty("background")
                    .GetProperty("url")
                    .ToString();
                Logger.Debug($"Background URL (fallback): {fallback}", "API");
                return fallback;
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
            string url = $"https://hyp-api.mihoyo.com/hyp/hyp-connect/api/getGameBranches?launcher_id={profile.ApiLauncherId}&language=zh-cn&game_ids[]={profile.ApiGameId}";
            try
            {
                var data = await GetAPIData(url);
                var version = data.GetProperty("data")
                   .GetProperty("game_branches")[0]
                   .GetProperty("main")
                   .GetProperty("tag")
                   .ToString();
                Logger.Debug($"PKG version for {profile.Id}: {version}", "API");
                return version;
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to get PKG version: {ex.Message}", "API");
                return string.Empty;
            }
        }
    }
}