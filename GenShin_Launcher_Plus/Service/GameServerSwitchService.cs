using System;
using System.IO;
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

        if (string.Equals(current.Value, target.Value, StringComparison.OrdinalIgnoreCase))
            return ServerSwitchResult.Current(target.Value, _session.Data.GetExactGamePath(target.Value));

        var exactPath = _session.Data.GetExactGamePath(target.Value);
        if (!IsTargetClient(profile, target, exactPath) || IsPathSharedByAnotherServer(target, exactPath))
        {
            // Discover an existing target client before presenting installation.
            // Never fall back to the previous server's legacy game-level path.
            var discovered = GameSearchService.FindGame(profile, target.Server);
            if (discovered != null && IsTargetClient(profile, target, discovered.Path) && !IsPathSharedByAnotherServer(target, discovered.Path))
            {
                exactPath = discovered.Path;
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

    private bool IsTargetClient(GameProfile profile, GameBiz target, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;

        var detected = GameSearchService.DetectServerFromConfig(path);
        return (string.IsNullOrWhiteSpace(detected) || string.Equals(detected, target.Server, StringComparison.OrdinalIgnoreCase))
            && File.Exists(Path.Combine(path, profile.GetExeName(target)));
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

    private string? FindBestHardLinkSource(GameBiz target)
    {
        var profile = GameProfiles.FindById(target.Game);
        if (profile == null) return null;

        Version? bestVersion = null;
        string? bestPath = null;
        foreach (var source in profile.GetSupportedServers())
        {
            if (source == target) continue;
            var path = _session.Data.GetExactGamePath(source.Value);
            if (!IsTargetClient(profile, source, path))
                path = GameSearchService.FindGame(profile, source.Server)?.Path;
            if (!IsTargetClient(profile, source, path)) continue;

            var version = GameStateService.GetLocalVersion(path!);
            if (bestPath == null || version != null && (bestVersion == null || version > bestVersion))
            {
                bestPath = path;
                bestVersion = version;
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
