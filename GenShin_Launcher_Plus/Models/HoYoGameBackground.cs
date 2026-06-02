using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GenShin_Launcher_Plus.Models
{
    /// <summary>
    /// 米哈游 HoYoPlay API 返回的图片数据
    /// </summary>
    public class HoYoImage
    {
        [JsonPropertyName("url")]
        public string Url { get; set; }
    }

    /// <summary>
    /// 单个背景图/视频项（getAllGameBasicInfo 返回的 backgrounds 数组元素）
    /// </summary>
    public class HoYoGameBackground
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("background")]
        public HoYoImage Background { get; set; }

        [JsonPropertyName("icon")]
        public HoYoImage Icon { get; set; }

        /// <summary>视频文件，可能为 null</summary>
        [JsonPropertyName("video")]
        public HoYoImage Video { get; set; }

        /// <summary>视频上方的叠加图片</summary>
        [JsonPropertyName("theme")]
        public HoYoImage Theme { get; set; }

        /// <summary>"BACKGROUND_TYPE_UNSPECIFIED" 或 "BACKGROUND_TYPE_VIDEO"</summary>
        [JsonPropertyName("type")]
        public string Type { get; set; }

        public bool IsVideo => Type == "BACKGROUND_TYPE_VIDEO" && Video != null;

        public bool IsCustom { get; set; }

        public string CacheFile { get; set; }

        public static HoYoGameBackground Custom(string filePath)
        {
            return new HoYoGameBackground
            {
                Id = "__custom__",
                IsCustom = true,
                CacheFile = filePath,
                Type = System.IO.Path.GetExtension(filePath)?.ToLower() switch
                {
                    ".mp4" or ".mkv" or ".webm" => "BACKGROUND_TYPE_VIDEO",
                    _ => "BACKGROUND_TYPE_UNSPECIFIED",
                },
            };
        }
    }

    /// <summary>
    /// getAllGameBasicInfo 返回的单个游戏的背景信息
    /// </summary>
    public class HoYoGameBackgroundInfo
    {
        [JsonPropertyName("game")]
        public HoYoGameId Game { get; set; }

        [JsonPropertyName("backgrounds")]
        public List<HoYoGameBackground> Backgrounds { get; set; }
    }

    public class HoYoGameId
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("biz")]
        public string Biz { get; set; }
    }
}