using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GenShin_Launcher_Plus.Helper;
using GenShin_Launcher_Plus.Core;
using GenShin_Launcher_Plus.Models;
using GenShin_Launcher_Plus.Models.HoYoPlay;

namespace GenShin_Launcher_Plus.Service;

/// <summary>
/// Detects game state by comparing local version with API versions.
/// </summary>
public static class GameStateService
{
    /// <summary>
    /// Get the local game version from config.ini
    /// </summary>
    public static Version? GetLocalVersion(string installPath)
    {
        try
        {
            var configPath = Path.Combine(installPath, "config.ini");
            if (!File.Exists(configPath)) return null;
            var content = File.ReadAllText(configPath);
            var match = Regex.Match(content, @"game_version=(.+)");
            if (match.Success && Version.TryParse(match.Groups[1].Value.Trim(), out var ver))
                return ver;
        }
        catch (Exception ex)
        {
            Logger.Warn($"GetLocalVersion failed: {ex.Message}", "GameState");
        }
        return null;
    }

    /// <summary>
    /// Check if the game exe exists in the install path
    /// </summary>
    public static bool IsGameInstalled(
        string installPath,
        GameProfile profile,
        GameBiz biz,
        bool trustExplicitPath = false)
    {
        if (string.IsNullOrEmpty(installPath) || !Directory.Exists(installPath))
            return false;
        var detectedServer = GameSearchService.DetectServerFromConfig(installPath);
        if (!string.IsNullOrEmpty(detectedServer) &&
            !string.Equals(detectedServer, biz.Server, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!trustExplicitPath && string.IsNullOrEmpty(detectedServer) &&
            (string.Equals(profile.CnExeName, profile.GlobalExeName, StringComparison.OrdinalIgnoreCase) || biz.IsBilibili()))
        {
            // A same-name executable cannot distinguish CN/global, and the
            // CN executable alone cannot distinguish official/Bilibili. A
            // matching per-biz HoYoPlay registry entry remains authoritative.
            var registered = GameSearchService.FindGame(profile, biz.Server);
            if (registered == null || !PathsEqual(installPath, registered.Path))
                return false;
        }
        var exe = profile.GetExeName(biz);
        return File.Exists(Path.Combine(installPath, exe));
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>
    /// Detect the current state of a game
    /// </summary>
    public static async Task<GameStateInfo> DetectGameStateAsync(DataModel data, string gameBiz, CancellationToken ct = default)
    {
        var info = new GameStateInfo { GameBiz = gameBiz };
        var profile = GameProfiles.FindById(new GameBiz(gameBiz).Game);
        if (profile == null)
        {
            info.State = GameState.NotInstalled;
            return info;
        }

        // Only the exact GameBiz path is valid here.  A legacy per-game
        // fallback can belong to a different server client.
        var installPath = data.GetExactGamePath(gameBiz);
        info.InstallPath = installPath;
        // This is the path stored explicitly for this GameBiz.  An older or
        // manually selected hard-link directory may not contain channel
        // metadata, so trust the mapping while still rejecting a positively
        // detected server mismatch and requiring the expected executable.
        info.IsInstalled = IsGameInstalled(
            installPath,
            profile,
            new GameBiz(gameBiz),
            trustExplicitPath: true);
        info.LocalVersion = GetLocalVersion(installPath);

        if (!info.IsInstalled)
        {
            info.State = GameState.NotInstalled;
            // Still check for latest version
            var (latest, predownload) = await HoYoPlayApiService.GetLatestVersionsAsync(gameBiz, ct).ConfigureAwait(false);
            info.LatestVersion = latest;
            info.PreDownloadVersion = predownload;
            return info;
        }

        // Game is installed, check versions
        var (latestVer, predownloadVer) = await HoYoPlayApiService.GetLatestVersionsAsync(gameBiz, ct).ConfigureAwait(false);
        info.LatestVersion = latestVer;
        info.PreDownloadVersion = predownloadVer;

        if (info.LocalVersion != null && info.LatestVersion != null)
        {
            if (Version.TryParse(info.LatestVersion, out var latest) &&
                info.LocalVersion < latest)
            {
                info.State = GameState.NeedUpdate;
            }
            else if (info.PreDownloadVersion != null)
            {
                info.State = IsPreDownloadFinished(installPath, info.LocalVersion?.ToString(), info.PreDownloadVersion)
                    ? GameState.Ready
                    : GameState.PreDownloadAvailable;
            }
            else
            {
                info.State = GameState.Ready;
            }
        }
        else if (info.PreDownloadVersion != null)
        {
            info.State = IsPreDownloadFinished(installPath, info.LocalVersion?.ToString(), info.PreDownloadVersion)
                ? GameState.Ready
                : GameState.PreDownloadAvailable;
        }
        else
        {
            info.State = GameState.Ready;
        }

        Logger.Debug($"GameState: {gameBiz} state={info.State} local={info.LocalVersion} latest={info.LatestVersion} predownload={info.PreDownloadVersion}", "GameState");
        return info;
    }

    public static bool IsPreDownloadFinished(string installPath, string? localVersion, string? preDownloadVersion)
    {
        if (string.IsNullOrWhiteSpace(installPath) || string.IsNullOrWhiteSpace(preDownloadVersion))
            return false;

        try
        {
            var configPath = Path.Combine(installPath, "config.ini");
            if (!File.Exists(configPath))
                return false;

            var content = File.ReadAllText(configPath);
            var match = Regex.Match(content, @"(?m)^\s*predownload\s*=\s*(.+?)\s*$");
            if (!match.Success)
                return false;

            var parts = match.Groups[1].Value.Split(',');
            if (parts.Length < 2)
                return false;

            var markedLocal = parts[0].Trim();
            var markedPre = parts[1].Trim();
            return string.Equals(markedPre, preDownloadVersion, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrWhiteSpace(localVersion) || string.Equals(markedLocal, localVersion, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Logger.Warn($"IsPreDownloadFinished failed: {ex.Message}", "GameState");
            return false;
        }
    }
}

public class GameStateInfo
{
    public string GameBiz { get; set; }
    public GameState State { get; set; }
    public bool IsInstalled { get; set; }
    public string InstallPath { get; set; }
    public Version LocalVersion { get; set; }
    public string LatestVersion { get; set; }
    public string PreDownloadVersion { get; set; }
}

