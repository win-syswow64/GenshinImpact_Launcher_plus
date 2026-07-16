using System.Collections.Generic;
using System.IO;

namespace GenShin_Launcher_Plus.Models
{
    /// <summary>
    /// Per-game metadata. Server-specific properties (exe name, data folder) are resolved via GameBiz.
    /// </summary>
    public class GameProfile
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public string CnExeName { get; set; }
        public string GlobalExeName { get; set; }
        public string CnDataFolder { get; set; }
        public string GlobalDataFolder { get; set; }
        public string CnRegistryKey { get; set; }
        public string GlobalRegistryKey { get; set; }
        public string CnSdkRegistryValue { get; set; }
        public string GlobalSdkRegistryValue { get; set; }
        public string DataRegistryValue { get; set; }
        public string ApiGameId { get; set; }
        public string ApiLauncherId { get; set; }
        public string? BilibiliSdkPath { get; set; }

        /// <summary>Pack URI to the official game icon.</summary>
        public string IconPath => $"/Images/{Id}_icon.png";
        /// <summary>Whether the game executable exists on disk.</summary>
        public bool IsInstalled
        {
            get
            {
                try
                {
                    var data = App.Current.DataModel;
                    if (data == null) return false;

                    // Each GameBiz owns an independent path and executable.
                    // Do not use the active game's executable or the legacy
                    // fallback path, otherwise only the first selected game is
                    // detected correctly in the top game selector.
                    foreach (var biz in GetSupportedServers())
                    {
                        var path = data.GetExactGamePath(biz.Value);
                        if (!string.IsNullOrWhiteSpace(path) && File.Exists(Path.Combine(path, GetExeName(biz))))
                            return true;
                    }
                }
                catch { }
                return false;
            }
        }

        /// <summary>Icon background color for the game selector (fallback).</summary>
        public string IconColor => Id switch
        {
            "genshin" => "#E5A93C",
            "starrail" => "#5B8DEF",
            "zzz" => "#00D4AA",
            "honkai3" => "#B060FF",
            _ => "#808080",
        };

        public string GetExeName(GameBiz biz) =>
            biz.IsChinaServer() || biz.IsBilibili() ? CnExeName : GlobalExeName;

        public string GetDataFolder(GameBiz biz) =>
            biz.IsChinaServer() || biz.IsBilibili() ? CnDataFolder : GlobalDataFolder;

        public string GetRegistryKey(GameBiz biz) =>
            biz.IsChinaServer() || biz.IsBilibili() ? CnRegistryKey : GlobalRegistryKey;

        /// <summary>
        /// Returns all supported GameBiz values for this game.
        /// </summary>
        public List<GameBiz> GetSupportedServers()
        {
            var servers = new List<GameBiz>
            {
                $"{Id}_cn",
                $"{Id}_global",
            };
            if (!string.IsNullOrEmpty(BilibiliSdkPath))
                servers.Add($"{Id}_bilibili");
            return servers;
        }
    }

    public static class GameProfiles
    {

        public static GameProfile Genshin { get; } = new()
        {
            Id = "genshin",
            DisplayName = @"原神",
            CnExeName = "YuanShen.exe",
            GlobalExeName = "GenshinImpact.exe",
            CnDataFolder = "YuanShen_Data",
            GlobalDataFolder = "GenshinImpact_Data",
            CnRegistryKey = @"HKEY_CURRENT_USER\Software\miHoYo\原神",
            GlobalRegistryKey = @"HKEY_CURRENT_USER\Software\miHoYo\Genshin Impact",
            CnSdkRegistryValue = "MIHOYOSDK_ADL_PROD_CN_h3123967166",
            GlobalSdkRegistryValue = "MIHOYOSDK_ADL_PROD_OVERSEA_h1158948810",
            DataRegistryValue = "GENERAL_DATA_h2389025596",
            ApiGameId = "1Z8W5NHUQb",
            ApiLauncherId = "jGHBHlcOq1",
            BilibiliSdkPath = "Plugins/PCGameSDK.dll",
        };

        public static GameProfile StarRail { get; } = new()
        {
            Id = "starrail",
            DisplayName = @"崩坏：星穹铁道",
            CnExeName = "StarRail.exe",
            GlobalExeName = "StarRail.exe",
            CnDataFolder = "StarRail_Data",
            GlobalDataFolder = "StarRail_Data",
            CnRegistryKey = @"HKEY_CURRENT_USER\Software\miHoYo\崩坏：星穹铁道",
            GlobalRegistryKey = @"HKEY_CURRENT_USER\Software\Cognosphere\Star Rail",
            CnSdkRegistryValue = "MIHOYOSDK_ADL_PROD_CN_h3123967166",
            GlobalSdkRegistryValue = "MIHOYOSDK_ADL_PROD_OVERSEA_h1158948810",
            DataRegistryValue = "GENERAL_DATA_h2389025596",
            ApiGameId = "64kMb5iAWu",
            ApiLauncherId = "jGHBHlcOq1",
            BilibiliSdkPath = "Plugins/PCGameSDK.dll",
        };

        public static GameProfile ZZZ { get; } = new()
        {
            Id = "zzz",
            DisplayName = @"绝区零",
            CnExeName = "ZenlessZoneZero.exe",
            GlobalExeName = "ZenlessZoneZero.exe",
            CnDataFolder = "ZenlessZoneZero_Data",
            GlobalDataFolder = "ZenlessZoneZero_Data",
            CnRegistryKey = @"HKEY_CURRENT_USER\Software\miHoYo\绝区零",
            GlobalRegistryKey = @"HKEY_CURRENT_USER\Software\miHoYo\ZenlessZoneZero",
            CnSdkRegistryValue = "MIHOYOSDK_ADL_PROD_CN_h3123967166",
            GlobalSdkRegistryValue = "MIHOYOSDK_ADL_PROD_OVERSEA_h1158948810",
            DataRegistryValue = "GENERAL_DATA_h2389025596",
            ApiGameId = "x6znKlJ0xK",
            ApiLauncherId = "jGHBHlcOq1",
            BilibiliSdkPath = "Plugins/PCGameSDK.dll",
        };

        public static GameProfile Honkai3 { get; } = new()
        {
            Id = "honkai3",
            DisplayName = @"崩坏3",
            CnExeName = "BH3.exe",
            GlobalExeName = "BH3.exe",
            CnDataFolder = "BH3_Data",
            GlobalDataFolder = "BH3_Data",
            CnRegistryKey = @"HKEY_CURRENT_USER\Software\miHoYo\崩坏3",
            GlobalRegistryKey = @"HKEY_CURRENT_USER\Software\miHoYo\Honkai Impact 3rd",
            CnSdkRegistryValue = "MIHOYOSDK_ADL_PROD_CN_h3123967166",
            GlobalSdkRegistryValue = "MIHOYOSDK_ADL_PROD_OVERSEA_h1158948810",
            DataRegistryValue = "GENERAL_DATA_h2389025596",
            ApiGameId = "osvnlOc0S8",
            ApiLauncherId = "jGHBHlcOq1",
            BilibiliSdkPath = null,
        };

        public static List<GameProfile> All { get; } = new()
        {
            Genshin, StarRail, ZZZ, Honkai3,
        };

        public static GameProfile? FindById(string id)
        {
            foreach (var p in All)
                if (p.Id == id) return p;
            return null;
        }
    }
}
