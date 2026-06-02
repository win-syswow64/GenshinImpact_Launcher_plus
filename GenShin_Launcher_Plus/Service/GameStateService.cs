using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GenShin_Launcher_Plus.Helper;
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
    public static bool IsGameInstalled(string installPath, GameProfile profile, GameBiz biz)
    {
        if (string.IsNullOrEmpty(installPath) || !Directory.Exists(installPath))
            return false;
        var exe = profile.GetExeName(biz);
        return File.Exists(Path.Combine(installPath, exe));
    }

    /// <summary>
    /// Detect the current state of a game
    /// </summary>
    public static async Task<GameStateInfo> DetectGameStateAsync(string gameBiz, CancellationToken ct = default)
    {
        var info = new GameStateInfo { GameBiz = gameBiz };
        var profile = GameProfiles.FindById(new GameBiz(gameBiz).Game);
        if (profile == null)
        {
            info.State = GameState.NotInstalled;
            return info;
        }

        var installPath = App.Current.DataModel.GetGamePath(gameBiz);
        info.InstallPath = installPath;
        info.IsInstalled = IsGameInstalled(installPath, profile, new GameBiz(gameBiz));
        info.LocalVersion = GetLocalVersion(installPath);

        if (!info.IsInstalled)
        {
            info.State = GameState.NotInstalled;
            // Still check for latest version
            var (latest, predownload) = await HoYoPlayApiService.GetLatestVersionsAsync(gameBiz, ct);
            info.LatestVersion = latest;
            info.PreDownloadVersion = predownload;
            return info;
        }

        // Game is installed, check versions
        var (latestVer, predownloadVer) = await HoYoPlayApiService.GetLatestVersionsAsync(gameBiz, ct);
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
                info.State = GameState.PreDownloadAvailable;
            }
            else
            {
                info.State = GameState.Ready;
            }
        }
        else if (info.PreDownloadVersion != null)
        {
            info.State = GameState.PreDownloadAvailable;
        }
        else
        {
            info.State = GameState.Ready;
        }

        Logger.Debug($"GameState: {gameBiz} state={info.State} local={info.LocalVersion} latest={info.LatestVersion} predownload={info.PreDownloadVersion}", "GameState");
        return info;
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

