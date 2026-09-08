using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        public sealed record GameSearchResult(string Path, string Server);

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
            => FindGame(profile, server)?.Path;

        public static GameSearchResult? FindGame(GameProfile profile, string preferredServer, bool allowDifferentServer = false)
            => FindGames(profile, preferredServer, allowDifferentServer).FirstOrDefault();

        /// <summary>
        /// Enumerate every usable candidate instead of validating only the
        /// first directory containing a same-named executable. Several HoYo
        /// games use the same exe name for CN and global clients, which is
        /// especially common in sibling directories backed by hard links.
        /// </summary>
        public static IReadOnlyList<GameSearchResult> FindGames(
            GameProfile profile,
            string preferredServer,
            bool allowDifferentServer = false)
        {
            var candidates = new List<(string? Path, string? TrustedServerHint)>
            {
                // A per-biz HoYoPlay registry key is an authoritative hint.
                (TryHypRegistry(profile.Id, preferredServer), preferredServer),
            };
            candidates.AddRange(TryLauncherGames(profile, preferredServer).Select(path => ((string?)path, (string?)null)));
            // The legacy global registry is distinct; CN and Bilibili often
            // share keys and therefore still require config/path evidence.
            candidates.Add((TryGameConfigRegistry(profile, preferredServer),
                preferredServer == "global" ? "global" : null));
            candidates.AddRange(TryCommonPaths(profile, preferredServer).Select(path => ((string?)path, (string?)null)));

            var results = new List<GameSearchResult>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in candidates)
            {
                var result = Validate(candidate.Path, profile, preferredServer, allowDifferentServer, candidate.TrustedServerHint);
                if (result == null) continue;
                string normalized;
                try
                {
                    normalized = Path.GetFullPath(result.Path)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
                catch { continue; }
                if (seen.Add(normalized))
                    results.Add(result with { Path = normalized });
            }
            return results;
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
        static List<string> TryLauncherGames(GameProfile profile, string server)
        {
            var results = new List<string>();
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
                            results.Add(candidate);
                    }

                    // Keep scanning after a same-name client from a different
                    // server. Validation happens for every candidate later.
                    foreach (var dir in Directory.GetDirectories(gamesDir))
                    {
                        if (File.Exists(Path.Combine(dir, exeName)))
                            results.Add(dir);
                    }
                }
            }
            catch { }
            return results;
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
        static List<string> TryCommonPaths(GameProfile profile, string server)
        {
            var results = new List<string>();
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
                $"{profile.DisplayName} ({server})",
                $"{ToFolderHint(profile.Id)} ({server})",
            };
            foreach (var d in drives)
                foreach (var r in roots)
                    foreach (var n in names)
                    {
                        var p = Path.Combine(d, r, n);
                        if (File.Exists(Path.Combine(p, exe))) results.Add(p);
                    }
            return results;
        }

        static GameSearchResult? Validate(
            string? path,
            GameProfile profile,
            string preferredServer,
            bool allowDifferentServer,
            string? trustedServerHint)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return null;
            bool cnExe = File.Exists(Path.Combine(path, profile.CnExeName));
            bool globalExe = File.Exists(Path.Combine(path, profile.GlobalExeName));
            if (!cnExe && !globalExe) return null;

            var detectedServer = DetectServerFromConfig(path) ?? trustedServerHint;
            if (string.IsNullOrWhiteSpace(detectedServer))
            {
                // Different executable names can identify CN/global without
                // config.ini. Same-name clients and CN/Bilibili cannot be
                // safely assigned to a target server from the exe alone.
                if (!string.Equals(profile.CnExeName, profile.GlobalExeName, StringComparison.OrdinalIgnoreCase))
                {
                    if (globalExe && !cnExe) detectedServer = "global";
                    else if (cnExe && !globalExe && preferredServer != "bilibili") detectedServer = "cn";
                }

                if (string.IsNullOrWhiteSpace(detectedServer))
                {
                    if (!allowDifferentServer) return null;
                    detectedServer = "unknown";
                }
            }
            if (!allowDifferentServer && !string.Equals(detectedServer, preferredServer, StringComparison.OrdinalIgnoreCase))
                return null;

            string exe = detectedServer switch
            {
                "cn" or "bilibili" => profile.CnExeName,
                "global" => profile.GlobalExeName,
                _ => string.Empty,
            };
            if (!string.IsNullOrEmpty(exe) && !File.Exists(Path.Combine(path, exe)))
                return null;

            return new GameSearchResult(path, detectedServer);
        }

        public static string? DetectServerFromConfig(string gamePath)
        {
            try
            {
                var configPath = Path.Combine(gamePath, "config.ini");
                if (File.Exists(configPath))
                {
                    foreach (var rawLine in File.ReadLines(configPath))
                    {
                        var line = rawLine.Trim();
                        if (line.StartsWith("#") || line.StartsWith(";")) continue;
                        var index = line.IndexOf('=');
                        if (index <= 0) continue;
                        var key = line[..index].Trim();
                        var value = line[(index + 1)..].Trim().Trim('"');

                        if (key.Equals("cps", StringComparison.OrdinalIgnoreCase))
                        {
                            if (value.Contains("bilibili", StringComparison.OrdinalIgnoreCase)) return "bilibili";
                            if (value.Contains("hoyoverse", StringComparison.OrdinalIgnoreCase)) return "global";
                            if (value.Contains("mihoyo", StringComparison.OrdinalIgnoreCase)) return "cn";
                        }
                        else if (key.Equals("game_biz", StringComparison.OrdinalIgnoreCase))
                        {
                            if (value.EndsWith("_bilibili", StringComparison.OrdinalIgnoreCase)) return "bilibili";
                            if (value.EndsWith("_global", StringComparison.OrdinalIgnoreCase)) return "global";
                            if (value.EndsWith("_cn", StringComparison.OrdinalIgnoreCase)) return "cn";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                GenShin_Launcher_Plus.Helper.Logger.Warn($"Detect server from config failed: {ex.Message}", "Search");
            }

            // The launcher creates explicit sibling names such as
            // "Star Rail (global)". Preserve that useful hint if config.ini
            // is absent or incomplete.
            try
            {
                string directoryName = new DirectoryInfo(gamePath).Name;
                foreach (var server in new[] { "cn", "global", "bilibili" })
                {
                    if (directoryName.EndsWith($"({server})", StringComparison.OrdinalIgnoreCase))
                        return server;
                }
            }
            catch { }
            return null;
        }
    }
}
