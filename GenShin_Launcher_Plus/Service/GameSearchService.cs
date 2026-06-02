using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;
using GenShin_Launcher_Plus.Models;

namespace GenShin_Launcher_Plus.Service
{
    /// <summary>
    /// Searches for installed miHoYo/HoYoverse game clients.
    /// Priority: HYP registry -> Uninstall registry + launcher scan -> common paths.
    /// </summary>
    public static class GameSearchService
    {
        // Map our IDs to HoYoPlay internal biz codes
        private static string? ToHypGame(string gameId) => gameId switch
        {
            "genshin" => "hk4e", "starrail" => "hkrpg",
            "zzz" => "nap", "honkai3" => "bh3", _ => null,
        };

        // English folder names used by the launcher under games/
        private static string? ToFolderHint(string gameId) => gameId switch
        {
            "genshin" => "Genshin Impact",
            "starrail" => "Star Rail",
            "zzz" => "ZenlessZoneZero",
            "honkai3" => "Honkai Impact",
            _ => null,
        };

        public static string? FindGamePath(GameProfile profile, string server)
        {
            // 1. HYP per-game registry (new launcher, may not exist)
            string? path = TryHypRegistry(profile.Id, server);
            if (Validate(path, profile, server)) return path;

            // 2. Launcher directories from Uninstall registry -> scan games/
            path = TryLauncherGames(profile, server);
            if (Validate(path, profile, server)) return path;

            // 3. Old game-config registry keys (GameProfile.CnRegistryKey etc.)
            path = TryGameConfigRegistry(profile, server);
            if (Validate(path, profile, server)) return path;

            // 4. Common brute-force paths
            path = TryCommonPaths(profile, server);
            if (Validate(path, profile, server)) return path;

            return null;
        }

        // ---------- 1. HYP per-game registry ----------
        static string? TryHypRegistry(string gameId, string server)
        {
            try
            {
                string? hyp = ToHypGame(gameId);
                if (hyp == null) return null;
                string biz = $"{hyp}_{server}";
                string key = server is "cn" or "bilibili"
                    ? $@"HKEY_CURRENT_USER\Software\miHoYo\HYP\1_1\{biz}"
                    : $@"HKEY_CURRENT_USER\Software\Cognosphere\HYP\1_0\{biz}";
                // Match Starward: use Registry.GetValue with full HKEY path
                var val = Registry.GetValue(key, "GameInstallPath", null) as string;
                if (!string.IsNullOrWhiteSpace(val)) return val;
            }
            catch { }
            return null;
        }

        // ---------- 2. Scan launcher games/ directories ----------
        static string? TryLauncherGames(GameProfile profile, string server)
        {
            try
            {
                var launcherDirs = FindLauncherDirectories();
                string exeName = server is "cn" or "bilibili" ? profile.CnExeName : profile.GlobalExeName;
                string? hint = ToFolderHint(profile.Id);

                foreach (var launcherDir in launcherDirs)
                {
                    string gamesDir = Path.Combine(launcherDir, "games");
                    if (!Directory.Exists(gamesDir)) continue;

                    // Try exact folder hint match: "{hint} Game"
                    if (hint != null)
                    {
                        string candidate = Path.Combine(gamesDir, $"{hint} Game");
                        if (File.Exists(Path.Combine(candidate, exeName)))
                            return candidate;
                    }

                    // Brute-force: scan every subfolder for the exe
                    foreach (var dir in Directory.GetDirectories(gamesDir))
                    {
                        if (File.Exists(Path.Combine(dir, exeName)))
                            return dir;
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Find all launcher install directories from the Uninstall registry.
        /// Searches for keys like hk4e_cn_1_1_*_production, HYP_1_1_cn, etc.
        /// </summary>
        static List<string> FindLauncherDirectories()
        {
            var dirs = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] roots =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
            };
            // Game biz prefixes to match
            string[] prefixes = { "hk4e_", "hkrpg_", "nap_", "bh3_", "HYP_" };

            foreach (string root in roots)
            {
                using var baseKey = Registry.LocalMachine.OpenSubKey(root);
                if (baseKey == null) continue;

                foreach (string subName in baseKey.GetSubKeyNames())
                {
                    bool isGame = false;
                    foreach (var p in prefixes)
                    {
                        if (subName.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                        { isGame = true; break; }
                    }
                    if (!isGame) continue;

                    using var sub = baseKey.OpenSubKey(subName);
                    if (sub == null) continue;

                    var loc = sub.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrWhiteSpace(loc) && seen.Add(loc) && Directory.Exists(loc))
                        dirs.Add(loc);

                    var uninst = sub.GetValue("UninstallString") as string;
                    if (!string.IsNullOrWhiteSpace(uninst))
                    {
                        var dir = Path.GetDirectoryName(uninst.Trim('"'));
                        if (!string.IsNullOrWhiteSpace(dir) && seen.Add(dir) && Directory.Exists(dir))
                            dirs.Add(dir);
                    }
                }
            }
            return dirs;
        }

        // ---------- 3. Old game-config registry ----------
        static string? TryGameConfigRegistry(GameProfile profile, string server)
        {
            try
            {
                string regKey = server is "cn" or "bilibili"
                    ? profile.CnRegistryKey : profile.GlobalRegistryKey;
                if (string.IsNullOrEmpty(regKey)) return null;
                var val = Registry.GetValue(regKey, "InstallPath", null)
                       ?? Registry.GetValue(regKey, "GamePath", null);
                if (val is string s && !string.IsNullOrWhiteSpace(s)) return s;
            }
            catch { }
            return null;
        }

        // ---------- 4. Common paths ----------
        static string? TryCommonPaths(GameProfile profile, string server)
        {
            string exe = server is "cn" or "bilibili" ? profile.CnExeName : profile.GlobalExeName;
            string[] drives = { @"C:", @"D:", @"E:", @"F:" };
            string[] roots =
            {
                @"Program Files\miHoYo",
                @"Program Files\HoYoverse",
                @"Program Files\miHoYo Launcher\games",
                @"Program Files\HoYoPlay\games",
                @"Program Files (x86)\miHoYo",
            };
            string[] names =
            {
                profile.DisplayName,
                profile.Id,
                Path.GetFileNameWithoutExtension(exe),
                $"{ToFolderHint(profile.Id)} Game",
            };
            foreach (var d in drives)
                foreach (var r in roots)
                    foreach (var n in names)
                    {
                        var p = Path.Combine(d, r, n);
                        if (File.Exists(Path.Combine(p, exe))) return p;
                    }
            return null;
        }

        static bool Validate(string? path, GameProfile profile, string server)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return false;
            string exe = server is "cn" or "bilibili" ? profile.CnExeName : profile.GlobalExeName;
            return File.Exists(Path.Combine(path, exe));
        }
    }
}