using System.Text.Json.Serialization;

namespace GenShin_Launcher_Plus.Models.HoYoPlay;

/// <summary>
/// HoYoPlay API game identifier mapping
/// </summary>
public static class HoYoPlayGameMap
{
    public static string? GetApiGameId(string gameBiz)
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

    public static string? GetLauncherId(string gameBiz)
    {
        return gameBiz switch
        {
            var s when s.EndsWith("_cn") => "jGHBHlcOq1",
            var s when s.EndsWith("_global") => "VYTpXlbWo8",
            "genshin_bilibili" => "umfgRO5gh5",
            "starrail_bilibili" => "6P5gHMNyK3",
            "zzz_bilibili" => "xV0f4r1GT0",
            _ => null,
        };
    }

    public static string GetApiBaseUrl(string gameBiz)
    {
        if (gameBiz.EndsWith("_global"))
            return "https://sg-hyp-api.hoyoverse.com/hyp/hyp-connect/api/";
        return "https://hyp-api.mihoyo.com/hyp/hyp-connect/api/";
    }

    public static string GetSophonBaseUrl(string gameBiz)
    {
        if (gameBiz.EndsWith("_global"))
            return "https://sg-downloader-api.hoyoverse.com/downloader/sophon_chunk/api/";
        return "https://downloader-api.mihoyo.com/downloader/sophon_chunk/api/";
    }

    public static (int Channel, int SubChannel) GetChannelInfo(string gameBiz)
    {
        if (gameBiz.EndsWith("_bilibili"))
            return (14, 0);
        if (gameBiz.EndsWith("_global"))
            return (1, 0);
        return (1, 1);
    }

    /// <summary>
    /// Map launcher gameBiz to HoYoPlay internal biz string
    /// </summary>
    public static string ToHoYoPlayBiz(string gameBiz)
    {
        return gameBiz switch
        {
            "genshin_cn" => "hk4e_cn",
            "genshin_global" => "hk4e_global",
            "genshin_bilibili" => "hk4e_bilibili",
            "starrail_cn" => "hkrpg_cn",
            "starrail_global" => "hkrpg_global",
            "starrail_bilibili" => "hkrpg_bilibili",
            "zzz_cn" => "nap_cn",
            "zzz_global" => "nap_global",
            "zzz_bilibili" => "nap_bilibili",
            "honkai3_cn" => "bh3_cn",
            "honkai3_global" => "bh3_global",
            _ => gameBiz,
        };
    }
}
