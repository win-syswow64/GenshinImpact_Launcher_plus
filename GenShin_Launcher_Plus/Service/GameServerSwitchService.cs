using System;
using System.IO;
using System.Linq;
using GenShin_Launcher_Plus.Core;
using GenShin_Launcher_Plus.Helper;
using GenShin_Launcher_Plus.Models;

namespace GenShin_Launcher_Plus.Service;

/// <summary>
/// Switches the active GameBiz without ever converting another server's client
/// in place.  This follows the same unit of work as Starward: game + server is
/// one install target, and downloader operations are subsequently executed for
/// that target.
/// </summary>
public sealed class GameServerSwitchService
{
    private readonly ILauncherSession _session;

    public GameServerSwitchService(ILauncherSession session)
    {
        _session = session;
    }

    public ServerSwitchResult Switch(string targetGameBiz)
    {
        var current = _session.Data.ActiveBiz;
        var target = new GameBiz(targetGameBiz);
        if (!target.IsKnown() || !string.Equals(current.Game, target.Game, StringComparison.OrdinalIgnoreCase))
            return ServerSwitchResult.Invalid("目标服务器无效。");

        var profile = GameProfiles.FindById(target.Game);
        if (profile == null || !profile.GetSupportedServers().Contains(target))
            return ServerSwitchResult.Invalid("当前游戏不支持此服务器。");

        var exactPath = _session.Data.GetExactGamePath(target.Value);
        bool isCurrent = string.Equals(current.Value, target.Value, StringComparison.OrdinalIgnoreCase);
        if (IsTargetClient(profile, target, exactPath, trustExplicitPath: true))
        {
            // Older launcher versions could map one legacy path to every
            // server. The user's explicit target selection is authoritative
            // when the directory has no contradictory channel metadata.
            ClearDuplicateMappings(target, exactPath!);
            return isCurrent
                ? ServerSwitchResult.Current(target.Value, exactPath)
                : CompleteInstalledSwitch(current, target, exactPath!);
        }

        if (!IsTargetClient(profile, target, exactPath, trustExplicitPath: true) ||
            IsPathSharedByAnotherServer(target, exactPath))
        {
            // Discover an existing target client before presenting installation.
            // Never fall back to the previous server's legacy game-level path.
            var discoveredPath = GameInstallService.GetKnownGamePathCandidates(_session.Data, profile, target.Server)
                .FirstOrDefault(path => IsTargetClient(profile, target, path, trustExplicitPath: false) && !IsPathSharedByAnotherServer(target, path));
            if (!string.IsNullOrWhiteSpace(discoveredPath))
            {
                exactPath = discoveredPath;
                _session.Data.SetGamePath(target.Value, exactPath);
                Logger.Info($"Discovered {target.Value} client: {exactPath}", "ServerSwitch");
            }
            else
            {
                // Clear a stale or cross-server mapping so the install dialog
                // starts in a new server-specific folder.
                if (!string.IsNullOrWhiteSpace(exactPath))
                    _session.Data.SetGamePath(target.Value, string.Empty);
                exactPath = string.Empty;
            }
        }

        _session.Data.ActiveGameBiz = target.Value;

        if (!string.IsNullOrWhiteSpace(exactPath))
        {
            Logger.Info($"Server switched: {current} -> {target} ({exactPath})", "ServerSwitch");
            return ServerSwitchResult.Installed(target.Value, exactPath);
        }

        var source = FindBestHardLinkSource(target);
        var recommendedPath = GameInstallService.GetDefaultInstallDir(_session.Data, target.Value);
        Logger.Info($"Server switched: {current} -> {target}; target install is required. Hard-link source: {source ?? "none"}", "ServerSwitch");
        return ServerSwitchResult.InstallRequired(target.Value, recommendedPath, source);
    }

    private ServerSwitchResult CompleteInstalledSwitch(GameBiz current, GameBiz target, string exactPath)
    {
        _session.Data.ActiveGameBiz = target.Value;
        Logger.Info($"Server switched: {current} -> {target} ({exactPath})", "ServerSwitch");
        return ServerSwitchResult.Installed(target.Value, exactPath);
    }

    private bool IsTargetClient(
        GameProfile profile,
        GameBiz target,
        string? path,
        bool trustExplicitPath)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;
        return GameStateService.IsGameInstalled(path, profile, target, trustExplicitPath);
    }

    private static bool IsReusableSource(GameProfile profile, GameBiz expectedSource, string path)
    {
        if (!GameInstallService.IsUsableGameDirectory(profile, path)) return false;
        var detected = GameSearchService.DetectServerFromConfig(path);
        return string.IsNullOrWhiteSpace(detected) ||
            string.Equals(detected, expectedSource.Server, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsPathSharedByAnotherServer(GameBiz target, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        string normalized;
        try { normalized = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch { return true; }

        var profile = GameProfiles.FindById(target.Game);
        if (profile == null) return true;
        foreach (var other in profile.GetSupportedServers())
        {
            if (other == target) continue;
            var otherPath = _session.Data.GetExactGamePath(other.Value);
            if (string.IsNullOrWhiteSpace(otherPath)) continue;
            try
            {
                if (string.Equals(normalized,
                    Path.GetFullPath(otherPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch { }
        }

        return false;
    }

    private void ClearDuplicateMappings(GameBiz target, string path)
    {
        var profile = GameProfiles.FindById(target.Game);
        if (profile == null) return;

        foreach (var other in profile.GetSupportedServers())
        {
            if (other == target) continue;
            var otherPath = _session.Data.GetExactGamePath(other.Value);
            if (!PathsEqual(path, otherPath)) continue;

            _session.Data.SetGamePath(other.Value, string.Empty);
            Logger.Info($"Removed duplicate legacy path mapping for {other}: {otherPath}", "ServerSwitch");
        }
    }

    private static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;
        try
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch { return string.Equals(left, right, StringComparison.OrdinalIgnoreCase); }
    }

    private string? FindBestHardLinkSource(GameBiz target)
    {
        var profile = GameProfiles.FindById(target.Game);
        if (profile == null) return null;

        Version? bestVersion = null;
        string? bestPath = null;
        foreach (var source in profile.GetSupportedServers())
        {
            if (source == target) continue;
            foreach (var path in GameInstallService.GetKnownGamePathCandidates(_session.Data, profile, source.Server))
            {
                if (!IsReusableSource(profile, source, path)) continue;

                var version = GameStateService.GetLocalVersion(path);
                if (bestPath == null || version != null && (bestVersion == null || version > bestVersion))
                {
                    bestPath = path;
                    bestVersion = version;
                }
            }
        }

        return bestPath;
    }
}

public enum ServerSwitchStatus
{
    Current,
    Installed,
    InstallRequired,
    Invalid,
}

public sealed record ServerSwitchResult(
    ServerSwitchStatus Status,
    string TargetGameBiz,
    string? InstallPath,
    string? RecommendedInstallPath,
    string? HardLinkSourcePath,
    string? Message)
{
    public static ServerSwitchResult Current(string gameBiz, string? path) => new(ServerSwitchStatus.Current, gameBiz, path, null, null, null);
    public static ServerSwitchResult Installed(string gameBiz, string path) => new(ServerSwitchStatus.Installed, gameBiz, path, null, null, null);
    public static ServerSwitchResult InstallRequired(string gameBiz, string recommendedPath, string? hardLinkSource) => new(ServerSwitchStatus.InstallRequired, gameBiz, null, recommendedPath, hardLinkSource, null);
    public static ServerSwitchResult Invalid(string message) => new(ServerSwitchStatus.Invalid, string.Empty, null, null, null, message);
}
