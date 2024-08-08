using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace GenShin_Launcher_Plus.Core
{
    public class HtmlHelper
    {
       
        public static async Task<JsonElement> GetAPIData(String Url)
        {
            try
            {
                using HttpClient client = new();
                HttpResponseMessage response = await client.GetAsync(Url);
                response.EnsureSuccessStatusCode();
                string responseBody = await response.Content.ReadAsStringAsync();

                // Parse the JSON document and return the root element
                JsonDocument doc = JsonDocument.Parse(responseBody);
                return doc.RootElement.Clone(); // Clone the root element to avoid accessing a disposed object
            }
            catch (HttpRequestException e)
            {
                Console.WriteLine($"Request error: {e.Message}");
                throw;
            }
            catch (JsonException e)
            {
                Console.WriteLine($"JSON parsing error: {e.Message}");
                throw;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Unexpected error: {e.Message}");
                throw;
            }
        }

        public static async Task<string> GetInfoFromHtmlAsync(string tag)
        {
            string Url = "https://api.nahidaya.top/API/genshinimpact.php";
            try
            {
                JsonElement data = await GetAPIData(Url);
                return data.GetProperty(tag).ToString();
            }
            catch (KeyNotFoundException e)
            {
                Console.WriteLine($"Tag not found: {e.Message}");
                return string.Empty;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Unexpected error: {e.Message}");
                return string.Empty;
            }
        }

        public static async Task<string> GetBackgroundImageUrlAsync()
        {
            string Url = "https://hyp-api.mihoyo.com/hyp/hyp-connect/api/getAllGameBasicInfo?launcher_id=jGHBHlcOq1&language=zh-cn";
            try
            {
                JsonElement data = await GetAPIData(Url);
                return data.GetProperty("data")
                   .GetProperty("game_info_list")[2]
                   .GetProperty("backgrounds")[0]
                   .GetProperty("background")
                   .GetProperty("url")
                   .ToString();
            }
            catch (KeyNotFoundException e)
            {
                Console.WriteLine($"Tag not found: {e.Message}");
                return string.Empty;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Unexpected error: {e.Message}");
                return string.Empty;
            }
        }

        public static async Task<string> GetPkgVersionAsync()
        {
            string Url = "https://hyp-api.mihoyo.com/hyp/hyp-connect/api/getGamePackages?launcher_id=jGHBHlcOq1&language=zh-cn";
            try
            {
                JsonElement data = await GetAPIData(Url);
                return data.GetProperty("data")
                   .GetProperty("game_packages")[2]
                   .GetProperty("main")
                   .GetProperty("major")
                   .GetProperty("version")
                   .ToString();
            }
            catch (KeyNotFoundException e)
            {
                Console.WriteLine($"Tag not found: {e.Message}");
                return string.Empty;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Unexpected error: {e.Message}");
                return string.Empty;
            }
        }
    }
}
