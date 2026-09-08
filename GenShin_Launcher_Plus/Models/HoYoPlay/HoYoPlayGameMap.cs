using System;
using System.Collections.Generic;
using GenShin_Launcher_Plus.Models;

namespace GenShin_Launcher_Plus.Models.HoYoPlay;

/// <summary>
/// HoYoPlay API game identifier mapping. The app uses friendly ids
/// (genshin/starrail/zzz/honkai3), while HoYoPlay and Starward use
/// hk4e/hkrpg/nap/bh3.
/// </summary>
public static class HoYoPlayGameMap
{
    private sealed record GameMapEntry(
        string AppBiz,
        string HoYoPlayBiz,
        string GameId,
        string LauncherId,
        int Channel,
        int SubChannel);

    private const string ChinaOfficialLauncherId = "jGHBHlcOq1";
    private const string GlobalOfficialLauncherId = "VYTpXlbWo8";
    private const string BilibiliGenshinLauncherId = "umfgRO5gh5";
    private const string BilibiliStarRailLauncherId = "6P5gHMNyK3";
    private const string BilibiliZZZLauncherId = "xV0f4r1GT0";

    private static readonly Dictionary<string, GameMapEntry> Entries = new(StringComparer.OrdinalIgnoreCase)
    {
        ["genshin_cn"] = new("genshin_cn", "hk4e_cn", "1Z8W5NHUQb", ChinaOfficialLauncherId, 1, 1),
        ["genshin_global"] = new("genshin_global", "hk4e_global", "gopR6Cufr3", GlobalOfficialLauncherId, 1, 1),
        ["genshin_bilibili"] = new("genshin_bilibili", "hk4e_bilibili", "T2S0Gz4Dr2", BilibiliGenshinLauncherId, 14, 0),

        ["starrail_cn"] = new("starrail_cn", "hkrpg_cn", "64kMb5iAWu", ChinaOfficialLauncherId, 1, 1),
        ["starrail_global"] = new("starrail_global", "hkrpg_global", "4ziysqXOQ8", GlobalOfficialLauncherId, 1, 1),
        ["starrail_bilibili"] = new("starrail_bilibili", "hkrpg_bilibili", "EdtUqXfCHh", BilibiliStarRailLauncherId, 14, 0),

        ["zzz_cn"] = new("zzz_cn", "nap_cn", "x6znKlJ0xK", ChinaOfficialLauncherId, 1, 1),
        ["zzz_global"] = new("zzz_global", "nap_global", "U5hbdsT9W7", GlobalOfficialLauncherId, 1, 1),
        ["zzz_bilibili"] = new("zzz_bilibili", "nap_bilibili", "HXAFlmYa17", BilibiliZZZLauncherId, 14, 0),

        ["honkai3_cn"] = new("honkai3_cn", "bh3_cn", "osvnlOc0S8", ChinaOfficialLauncherId, 1, 1),
        ["honkai3_global"] = new("honkai3_global", "bh3_global", "5TIVvvcwtM", GlobalOfficialLauncherId, 1, 1),
    };

    private static readonly Dictionary<string, string> HoYoPlayBizToAppBiz = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hk4e_cn"] = "genshin_cn",
        ["hk4e_global"] = "genshin_global",
        ["hk4e_bilibili"] = "genshin_bilibili",
        ["hkrpg_cn"] = "starrail_cn",
        ["hkrpg_global"] = "starrail_global",
        ["hkrpg_bilibili"] = "starrail_bilibili",
        ["nap_cn"] = "zzz_cn",
        ["nap_global"] = "zzz_global",
        ["nap_bilibili"] = "zzz_bilibili",
        ["bh3_cn"] = "honkai3_cn",
        ["bh3_global"] = "honkai3_global",
    };

    public static IReadOnlyCollection<string> AllGameBiz => Entries.Keys;

    public static string? GetApiGameId(string gameBiz)
    {
        return TryGetEntry(gameBiz, out var entry) ? entry.GameId : null;
    }

    public static string? GetLauncherId(string gameBiz)
    {
        return TryGetEntry(gameBiz, out var entry) ? entry.LauncherId : null;
    }

    /// <summary>
    /// Select the HoYoPlay metadata/content API from the requested game
    /// channel. Unlike launcher artwork, game packages are server-specific.
    /// </summary>
    public static string GetApiBaseUrl(string gameBiz) =>
        new GameBiz(ToAppBiz(gameBiz)).IsGlobalServer()
            ? "https://sg-hyp-api.hoyoverse.com/hyp/hyp-connect/api/"
            : "https://hyp-api.mihoyo.com/hyp/hyp-connect/api/";

    /// <summary>Select the Sophon downloader API from the game channel.</summary>
    public static string GetSophonBaseUrl(string gameBiz) =>
        new GameBiz(ToAppBiz(gameBiz)).IsGlobalServer()
            ? "https://sg-downloader-api.hoyoverse.com/downloader/sophon_chunk/api/"
            : "https://downloader-api.mihoyo.com/downloader/sophon_chunk/api/";

    /// <summary>
    /// Region-based routing is intentionally reserved for launcher artwork.
    /// </summary>
    public static string GetRegionalArtworkApiBaseUrl(bool isChina) => isChina
        ? "https://hyp-api.mihoyo.com/hyp/hyp-connect/api/"
        : "https://sg-hyp-api.hoyoverse.com/hyp/hyp-connect/api/";

    /// <summary>
    /// Artwork follows the viewer's network region, so the launcher id and
    /// game id must be changed together with the artwork API host.
    /// </summary>
    public static string GetRegionalArtworkGameBiz(string gameBiz, bool isChina)
    {
        var appBiz = new GameBiz(ToAppBiz(gameBiz));
        return $"{appBiz.Game}_{(isChina ? "cn" : "global")}";
    }

    public static (int Channel, int SubChannel) GetChannelInfo(string gameBiz)
    {
        return TryGetEntry(gameBiz, out var entry) ? (entry.Channel, entry.SubChannel) : (1, 1);
    }

    /// <summary>
    /// Map launcher gameBiz to HoYoPlay internal biz string.
    /// </summary>
    public static string ToHoYoPlayBiz(string gameBiz)
    {
        return TryGetEntry(gameBiz, out var entry) ? entry.HoYoPlayBiz : gameBiz;
    }

    public static string ToAppBiz(string gameBiz)
    {
        return TryGetEntry(gameBiz, out var entry) ? entry.AppBiz : gameBiz;
    }

    public static bool IsKnown(string gameBiz) => TryGetEntry(gameBiz, out _);

    private static bool TryGetEntry(string gameBiz, out GameMapEntry entry)
    {
        if (Entries.TryGetValue(gameBiz, out entry!))
            return true;
        if (HoYoPlayBizToAppBiz.TryGetValue(gameBiz, out var appBiz) && Entries.TryGetValue(appBiz, out entry!))
            return true;
        return false;
    }
}
