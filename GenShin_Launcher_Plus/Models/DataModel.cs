using System;
using System.IO;
using System.Windows;
using GenShin_Launcher_Plus.Models;

namespace GenShin_Launcher_Plus.Core
{
    public class DataModel
    {
        public DataModel()
        {
            parser = new(@"Config\Setting.ini");
            // Migrate from old format if needed
            MigrateFromOldFormat();
        }

        IniParser parser { get; set; }

        private void MigrateFromOldFormat()
        {
            string oldBiz = parser.GetSetting("setup", "ActiveGameBiz", 0);
            if (!string.IsNullOrEmpty(oldBiz)) return; // already migrated

            string game = parser.GetSetting("setup", "SelectedGame", 0);
            if (string.IsNullOrEmpty(game)) game = "genshin";

            // Read cps from the game's Config.ini to determine old server
            string gamePath = parser.GetSetting("setup", "GamePath", 0);
            string cps = "mihoyo";
            if (!string.IsNullOrEmpty(gamePath))
            {
                string configIni = Path.Combine(gamePath, "Config.ini");
                if (File.Exists(configIni))
                {
                    var gp = new IniParser(configIni);
                    cps = gp.GetSetting("General", "cps", 0) ?? "mihoyo";
                }
            }

            string biz;
            if (cps.Contains("bilibili", StringComparison.OrdinalIgnoreCase))
                biz = $"{game}_bilibili";
            else if (cps.Contains("hoyoverse", StringComparison.OrdinalIgnoreCase))
                biz = $"{game}_global";
            else
                biz = $"{game}_cn";

            parser.AddSetting("setup", "ActiveGameBiz", biz);

            // Migrate per-game paths
            if (!string.IsNullOrEmpty(gamePath))
            {
                parser.AddSetting(biz, "GamePath", gamePath);
                parser.AddSetting(game, "GamePath", gamePath);
            }

            // Migrate per-game paths for other games
            foreach (var profile in GameProfiles.All)
            {
                if (profile.Id == game) continue;
                string path = parser.GetSetting(profile.Id, "GamePath", 0);
                if (!string.IsNullOrEmpty(path))
                {
                    parser.AddSetting($"{profile.Id}_cn", "GamePath", path);
                    parser.AddSetting($"{profile.Id}_global", "GamePath", path);
                    if (profile.BilibiliSdkPath != null)
                        parser.AddSetting($"{profile.Id}_bilibili", "GamePath", path);
                }
            }

            parser.SaveSettings();
        }

        // === Language ===
        private string _ReadLang;
        public string ReadLang
        {
            get { _ReadLang = parser.GetSetting("setup", "Language", 0); return _ReadLang; }
            set { _ReadLang = value; parser.AddSetting("setup", "Language", _ReadLang); }
        }

        // === Active GameBiz (e.g. "genshin_cn", "starrail_global") ===
        private string _ActiveGameBiz;
        public string ActiveGameBiz
        {
            get
            {
                _ActiveGameBiz = parser.GetSetting("setup", "ActiveGameBiz", 0);
                if (string.IsNullOrEmpty(_ActiveGameBiz)) _ActiveGameBiz = "genshin_cn";
                return _ActiveGameBiz;
            }
            set
            {
                _ActiveGameBiz = value;
                parser.AddSetting("setup", "ActiveGameBiz", _ActiveGameBiz);
            }
        }

        public GameBiz ActiveBiz => new(ActiveGameBiz);

        public GameProfile ActiveGame
        {
            get
            {
                try
                {
                    string game = ActiveBiz.Game;
                    var profile = GameProfiles.FindById(game);
                    if (profile != null) return profile;
                }
                catch { }
                // Hardcoded fallback if static init fails
                try
                {
                    var list = GameProfiles.All;
                    if (list != null && list.Count > 0) return list[0];
                }
                catch { }
                return new GameProfile
                {
                    Id = "genshin", DisplayName = "Genshin",
                    CnExeName = "YuanShen.exe", GlobalExeName = "GenshinImpact.exe",
                    CnDataFolder = "YuanShen_Data", GlobalDataFolder = "GenshinImpact_Data",
                };
            }
        }

        // === Per-GameBiz path management ===
        public string GamePath
        {
            get => GetGamePath(ActiveGameBiz);
            set => SetGamePath(ActiveGameBiz, value);
        }

        public string GetGamePath(string gameBiz)
        {
            var path = parser.GetSetting(gameBiz, "GamePath", 0);
            if (!string.IsNullOrEmpty(path)) return path;

            // Fall back to game-level path (backward compat)
            var game = new GameBiz(gameBiz).Game;
            path = parser.GetSetting(game, "GamePath", 0);
            if (!string.IsNullOrEmpty(path)) return path;

            // Fall back to global path
            return parser.GetSetting("setup", "GamePath", 0);
        }

        public string GetExactGamePath(string gameBiz)
        {
            return parser.GetSetting(gameBiz, "GamePath", 0);
        }

        public void SetGamePath(string gameBiz, string path)
        {
            parser.AddSetting(gameBiz, "GamePath", path ?? string.Empty);
            parser.SaveSettings();
        }

        // === Background ===
        private string _BackgroundPath;
        public string BackgroundPath
        {
            get { _BackgroundPath = parser.GetSetting("setup", "BackgroundPath", 0); return _BackgroundPath; }
            set { _BackgroundPath = value; parser.AddSetting("setup", "BackgroundPath", _BackgroundPath); }
        }

        // === Resolution ===
        private string _Width;
        public string Width
        {
            get { _Width = parser.GetSetting("setup", "Width", 1); return _Width; }
            set { _Width = value; parser.AddSetting("setup", "Width", Convert.ToString(_Width)); }
        }

        private string _Height;
        public string Height
        {
            get { _Height = parser.GetSetting("setup", "Height"); return _Height; }
            set { _Height = value; parser.AddSetting("setup", "Height", Convert.ToString(_Height)); }
        }

        private double _MainWidth;
        public double MainWidth
        {
            get { _MainWidth = Convert.ToDouble(parser.GetSetting("setup", "MainWidth", 1)); return _MainWidth == 1 ? 0 : _MainWidth; }
            set { _MainWidth = value; parser.AddSetting("setup", "MainWidth", Convert.ToString(_MainWidth)); }
        }

        private double _MainHeight;
        public double MainHeight
        {
            get { _MainHeight = Convert.ToDouble(parser.GetSetting("setup", "MainHeight", 1)); return _MainHeight == 1 ? 0 : _MainHeight; }
            set { _MainHeight = value; parser.AddSetting("setup", "MainHeight", Convert.ToString(_MainHeight)); }
        }

        // === Image ===
        private string _ImageDate;
        public string ImageDate
        {
            get { _ImageDate = parser.GetSetting("setup", "ImageDate", 1); return _ImageDate; }
            set { _ImageDate = value; parser.AddSetting("setup", "ImageDate", Convert.ToString(_ImageDate)); SaveDataToFile(); }
        }

        private string _ImagePid;
        public string ImagePid
        {
            get { _ImagePid = parser.GetSetting("setup", "ImagePid", 0); return _ImagePid; }
            set { _ImagePid = value; parser.AddSetting("setup", "ImagePid", _ImagePid); }
        }

        // === Game settings ===
        private string _MaxFps;
        public string MaxFps
        {
            get { _MaxFps = parser.GetSetting("setup", "MaxFps", 0); return _MaxFps; }
            set { _MaxFps = value; parser.AddSetting("setup", "MaxFps", _MaxFps); }
        }

        private string _SwitchUser;
        public string SwitchUser
        {
            get
            {
                _SwitchUser = parser.GetSetting(ActiveGameBiz, "SwitchUser", 0);
                return _SwitchUser;
            }
            set
            {
                _SwitchUser = value;
                parser.AddSetting(ActiveGameBiz, "SwitchUser", _SwitchUser);
            }
        }

        private bool _IsPopup;
        public bool IsPopup
        {
            get { _IsPopup = Convert.ToBoolean(parser.GetSetting("setup", "isPopup", 2)); return _IsPopup; }
            set { _IsPopup = value; parser.AddSetting("setup", "isPopup", Convert.ToString(_IsPopup)); }
        }

        private bool _IsWebBg;
        public bool IsWebBg
        {
            get { _IsWebBg = Convert.ToBoolean(parser.GetSetting("setup", "isWebBg", 2)); return _IsWebBg; }
            set { _IsWebBg = value; parser.AddSetting("setup", "isWebBg", Convert.ToString(_IsWebBg)); }
        }

        private bool _IsLocalDailyImage;
        public bool IsLocalDailyImage
        {
            get { _IsLocalDailyImage = Convert.ToBoolean(parser.GetSetting("setup", "IsLocalDailyImage", 2)); return _IsLocalDailyImage; }
            set { _IsLocalDailyImage = value; parser.AddSetting("setup", "IsLocalDailyImage", Convert.ToString(_IsLocalDailyImage)); }
        }

        private ushort _FullSize;
        public ushort FullSize
        {
            get { _FullSize = Convert.ToUInt16(parser.GetSetting("setup", "FullSize", 1)); return _FullSize; }
            set { _FullSize = value; parser.AddSetting("setup", "FullSize", Convert.ToString(_FullSize)); }
        }

        private bool _IsUnFPS;
        public bool IsUnFPS
        {
            get { _IsUnFPS = Convert.ToBoolean(parser.GetSetting("setup", "isUnFPS", 2)); return _IsUnFPS; }
            set { _IsUnFPS = value; parser.AddSetting("setup", "isUnFPS", Convert.ToString(_IsUnFPS)); }
        }

        private bool _IsCloseUpdate;
        public bool IsCloseUpdate
        {
            get { _IsCloseUpdate = Convert.ToBoolean(parser.GetSetting("setup", "IsCloseUpdate", 2)); return _IsCloseUpdate; }
            set { _IsCloseUpdate = value; parser.AddSetting("setup", "IsCloseUpdate", Convert.ToString(_IsCloseUpdate)); }
        }

        private bool _NavScreenshots = true;
        public bool NavScreenshots
        {
            get { _NavScreenshots = Convert.ToBoolean(parser.GetSetting("setup", "NavScreenshots", 2)); return _NavScreenshots; }
            set { _NavScreenshots = value; parser.AddSetting("setup", "NavScreenshots", Convert.ToString(_NavScreenshots)); }
        }

        private bool _NavQQGroup = true;
        public bool NavQQGroup
        {
            get { _NavQQGroup = Convert.ToBoolean(parser.GetSetting("setup", "NavQQGroup", 2)); return _NavQQGroup; }
            set { _NavQQGroup = value; parser.AddSetting("setup", "NavQQGroup", Convert.ToString(_NavQQGroup)); }
        }

        private bool _NavAbout = true;
        public bool NavAbout
        {
            get { _NavAbout = Convert.ToBoolean(parser.GetSetting("setup", "NavAbout", 2)); return _NavAbout; }
            set { _NavAbout = value; parser.AddSetting("setup", "NavAbout", Convert.ToString(_NavAbout)); }
        }

        private bool _NavLanguage = true;
        public bool NavLanguage
        {
            get { _NavLanguage = Convert.ToBoolean(parser.GetSetting("setup", "NavLanguage", 2)); return _NavLanguage; }
            set { _NavLanguage = value; parser.AddSetting("setup", "NavLanguage", Convert.ToString(_NavLanguage)); }
        }

        private bool _IsRunThenClose;
        public bool IsRunThenClose
        {
            get { _IsRunThenClose = Convert.ToBoolean(parser.GetSetting("setup", "IsRunThenClose", 2)); return _IsRunThenClose; }
            set { _IsRunThenClose = value; parser.AddSetting("setup", "IsRunThenClose", Convert.ToString(_IsRunThenClose)); }
        }

        private string _AccentColor;
        public string AccentColor
        {
            get { _AccentColor = parser.GetSetting("setup", "AccentColor", 0); if (string.IsNullOrEmpty(_AccentColor)) _AccentColor = "#FF69B4"; return _AccentColor; }
            set { _AccentColor = value; parser.AddSetting("setup", "AccentColor", _AccentColor); }
        }

        private bool _UseXunkongWallpaper;
        public bool UseXunkongWallpaper
        {
            get { _UseXunkongWallpaper = Convert.ToBoolean(parser.GetSetting("setup", "UseXunkongWallpaper", 2)); return _UseXunkongWallpaper; }
            set { _UseXunkongWallpaper = value; parser.AddSetting("setup", "UseXunkongWallpaper", Convert.ToString(_UseXunkongWallpaper)); }
        }

        // === Legacy: kept for backward compat, replaced by ActiveGameBiz ===
        private string _SelectedGame;
        public string SelectedGame
        {
            get { _SelectedGame = parser.GetSetting("setup", "SelectedGame", 0); if (string.IsNullOrEmpty(_SelectedGame)) _SelectedGame = "genshin"; return _SelectedGame; }
            set { _SelectedGame = value; parser.AddSetting("setup", "SelectedGame", _SelectedGame); }
        }

        public void EXEname(string value)
        {
            parser.AddSetting("setup", "LauncherPlusName", value);
            parser.SaveSettings();
        }

        public void SaveDataToFile()
        {
            parser.SaveSettings();
        }
        // === Per-Game Background ===
        public string GetSelectedBackgroundId(string gameId)
        {
            return parser.GetSetting($"bg_{gameId}", "SelectedBgId", 0) ?? "";
        }

        public void SetSelectedBackgroundId(string gameId, string bgId)
        {
            parser.AddSetting($"bg_{gameId}", "SelectedBgId", bgId ?? "");
        }

        public string GetCustomBackground(string gameId)
        {
            return parser.GetSetting($"bg_{gameId}", "CustomBgPath", 0) ?? "";
        }

        public void SetCustomBackground(string gameId, string path)
        {
            parser.AddSetting($"bg_{gameId}", "CustomBgPath", path ?? "");
        }
    }
}
