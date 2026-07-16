using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GenShin_Launcher_Plus.Models.HoYoPlay;

/// <summary>Official HoYoPlay getGameContent response payload.</summary>
public sealed class GameContentResponse
{
    [JsonPropertyName("content")]
    public GameContentInfo? Content { get; set; }
}

public sealed class GameContentInfo
{
    [JsonPropertyName("banners")]
    public List<GameContentBanner>? Banners { get; set; }

    [JsonPropertyName("posts")]
    public List<GameContentPost>? Posts { get; set; }
}

public sealed class GameContentBanner
{
    [JsonPropertyName("image")]
    public GameContentImage? Image { get; set; }
}

public sealed class GameContentImage
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("link")]
    public string? Link { get; set; }
}

public sealed class GameContentPost
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("link")]
    public string? Link { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }
}

/// <summary>News item displayed by the launcher home page.</summary>
public sealed class GameNewsItem
{
    public string Title { get; init; } = "";
    public string Link { get; init; } = "";
    public string Date { get; init; } = "";
    public string Category { get; init; } = "资讯";
}

public sealed class GameBannerItem
{
    public string ImageUrl { get; init; } = "";
    public string Link { get; init; } = "";
}

public sealed class GameLauncherContent
{
    public List<GameBannerItem> Banners { get; init; } = new();
    public List<GameNewsItem> News { get; init; } = new();
}
