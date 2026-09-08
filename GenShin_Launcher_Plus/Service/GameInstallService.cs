using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GenShin_Launcher_Plus.Helper;
using GenShin_Launcher_Plus.Core;
using GenShin_Launcher_Plus.Models;
using GenShin_Launcher_Plus.Models.HoYoPlay;
using ZstdSharp;

namespace GenShin_Launcher_Plus.Service;

public class GameInstallService : INotifyPropertyChanged
{
    private readonly ILauncherSession _session;
    private static readonly HttpClient[] _httpClients;
    private static readonly int _maxParallelism;
    private const int MD5_BUFFER_SIZE = 1 << 19; // 512KB, matches Starward's MD5 check buffer

    static GameInstallService()
    {
        int cpuCores = Environment.ProcessorCount;
        _maxParallelism = Math.Clamp(cpuCores * 2, 4, 16);
        Logger.Info($"Download parallelism: {_maxParallelism} (CPU cores: {cpuCores})", "Install");
        _httpClients = new HttpClient[_maxParallelism];
        for (int i = 0; i < _maxParallelism; i++)
        {
            _httpClients[i] = new HttpClient(new SocketsHttpHandler
            {
                MaxConnectionsPerServer = 2,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                ConnectTimeout = TimeSpan.FromSeconds(15),
            }) { Timeout = TimeSpan.FromMinutes(5) };
        }
    }

    public GameInstallService(ILauncherSession session)
    {
        _session = session;
    }

    // === Bindable ===
    private GameInstallState _state = GameInstallState.Idle;
    public GameInstallState State
    {
        get => _state;
        set
        {
            _state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsInstalling));
            OnPropertyChanged(nameof(IsTaskVisible));
            OnPropertyChanged(nameof(CanPause));
            OnPropertyChanged(nameof(CanContinue));
            OnPropertyChanged(nameof(StateText));
        }
    }

    private string _statusText = "";
    public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }

    private double _progressPercent;
    public double ProgressPercent { get => _progressPercent; set { _progressPercent = Math.Clamp(value, 0, 100); OnPropertyChanged(); ProgressText = $"{_progressPercent:F0}%"; } }

    private string _progressText = "0%";
    public string ProgressText { get => _progressText; set { _progressText = value; OnPropertyChanged(); } }

    private string _downloadSpeedText = "";
    public string DownloadSpeedText { get => _downloadSpeedText; set { _downloadSpeedText = value; OnPropertyChanged(); } }

    private string _bytesProgressText = "";
    public string BytesProgressText { get => _bytesProgressText; set { _bytesProgressText = value; OnPropertyChanged(); } }

    private string _remainingTimeText = "";
    public string RemainingTimeText { get => _remainingTimeText; set { _remainingTimeText = value; OnPropertyChanged(); } }

    private long _totalBytes;
    public long TotalBytes { get => _totalBytes; set { _totalBytes = value; OnPropertyChanged(); } }

    private long _downloadedBytes;
    public long DownloadedBytes { get => _downloadedBytes; set { _downloadedBytes = value; OnPropertyChanged(); } }

    private string _errorText = "";
    public string ErrorText { get => _errorText; set { _errorText = value; OnPropertyChanged(); } }

    public bool IsInstalling => State is GameInstallState.Downloading or GameInstallState.Preparing or GameInstallState.Extracting or GameInstallState.Verifying;

    /// <summary>Whether this task should remain visible in the launcher panel.</summary>
    public bool IsTaskVisible => State is not GameInstallState.Idle and not GameInstallState.Finished;
    public bool CanPause => IsInstalling;
    public bool CanContinue => State is GameInstallState.Paused or GameInstallState.Error;

    private GameInstallOperation? _operation;
    public GameInstallOperation? Operation
    {
        get => _operation;
        private set { _operation = value; OnPropertyChanged(); OnPropertyChanged(nameof(OperationText)); }
    }

    public string ActiveGameBiz { get; private set; } = "";
    public string ActiveInstallPath { get; private set; } = "";
    public string OperationText => Operation switch
    {
        GameInstallOperation.Install => "下载安装",
        GameInstallOperation.Update => "游戏更新",
        GameInstallOperation.PreDownload => "预下载",
        GameInstallOperation.Verify => "完整性校验",
        GameInstallOperation.Repair => "资源修复",
        _ => "游戏任务",
    };

    private GameResourceVerificationResult? _lastVerification;
    public GameResourceVerificationResult? LastVerification
    {
        get => _lastVerification;
        private set { _lastVerification = value; OnPropertyChanged(); OnPropertyChanged(nameof(VerificationSummary)); }
    }

    public string VerificationSummary => LastVerification == null
        ? ""
        : LastVerification.IsHealthy
            ? $"已校验 {LastVerification.TotalFiles} 个文件，资源完整"
            : $"已校验 {LastVerification.TotalFiles} 个文件：缺失 {LastVerification.MissingFiles}，异常 {LastVerification.InvalidFiles}";

    public string StateText => State switch
    {
        GameInstallState.Idle => "",
        GameInstallState.Preparing => "准备中...",
        GameInstallState.Downloading => $"下载中 {ProgressText}",
        GameInstallState.Extracting => "解压中...",
        GameInstallState.Verifying => "校验中...",
        GameInstallState.Finished => "完成",
        GameInstallState.Error => "错误",
        GameInstallState.Paused => "已暂停",
        _ => "",
    };

    private CancellationTokenSource? _cts;
    private long _speedBytesAccumulator;
    private long _lastSpeedTickMs;
    private double _smoothedBytesPerSecond;
    private Timer? _speedTimer;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void StartSpeedTracking()
    {
        _speedBytesAccumulator = 0;
        _smoothedBytesPerSecond = 0;
        _lastSpeedTickMs = Environment.TickCount64;
        ProgressPercent = 0;
        DownloadSpeedText = "0 KB/s";
        BytesProgressText = $"{FormatBytes(Volatile.Read(ref _downloadedBytes))} / {FormatBytes(Volatile.Read(ref _totalBytes))}";
        RemainingTimeText = "--:--:--";
        _speedTimer?.Dispose();
        _speedTimer = new Timer(_ =>
        {
            try
            {
                var now = Environment.TickCount64; var elapsed = now - _lastSpeedTickMs;
                if (elapsed > 0)
                {
                    var speed = Interlocked.Exchange(ref _speedBytesAccumulator, 0) * 1000 / elapsed;
                    if (speed > 0)
                        _smoothedBytesPerSecond = _smoothedBytesPerSecond <= 0 ? speed : _smoothedBytesPerSecond * 0.7 + speed * 0.3;
                    var downloaded = Volatile.Read(ref _downloadedBytes);
                    var total = Volatile.Read(ref _totalBytes);
                    DownloadSpeedText = speed > 0 ? FormatSpeed(speed) : "0 KB/s";
                    BytesProgressText = $"{FormatBytes(downloaded)} / {FormatBytes(total)}";
                    RemainingTimeText = FormatRemainingTime(total - downloaded, _smoothedBytesPerSecond);
                    _lastSpeedTickMs = now;
                }
            }
            catch { }
        }, null, 1000, 1000);
    }

    private void StopSpeedTracking()
    {
        _speedTimer?.Dispose();
        _speedTimer = null;
        DownloadSpeedText = "- KB/s";
        RemainingTimeText = "--:--:--";
    }

    private void ReportBytes(long n)
    {
        var downloaded = Interlocked.Add(ref _downloadedBytes, n);
        Interlocked.Add(ref _speedBytesAccumulator, n);
        var total = Volatile.Read(ref _totalBytes);
        if (total > 0)
            ProgressPercent = Math.Min(100, (double)downloaded / total * 100);
    }

    private void BeginOperation(GameInstallOperation operation, string gameBiz, string installPath)
    {
        Operation = operation;
        if (operation == GameInstallOperation.Verify)
            LastVerification = null;
        ActiveGameBiz = gameBiz;
        ActiveInstallPath = installPath;
        OnPropertyChanged(nameof(ActiveGameBiz));
        OnPropertyChanged(nameof(ActiveInstallPath));
    }

    // === Helpers ===

    public static List<string> GetExistingGameDrives(DataModel data)
    {
        var drives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in GameProfiles.All)
            foreach (var server in new[] { "cn", "global", "bilibili" })
            {
                foreach (var path in GetKnownGamePathCandidates(data, profile, server))
                {
                    if (!IsUsableGameDirectory(profile, path)) continue;
                    var root = Path.GetPathRoot(path);
                    if (!string.IsNullOrEmpty(root)) drives.Add(root);
                }
            }
        return drives.ToList();
    }

    internal static IEnumerable<string> GetKnownGamePathCandidates(DataModel data, GameProfile profile, string server)
    {
        var gameBiz = $"{profile.Id}_{server}";
        return GetKnownGamePathCandidates(profile, server, new string?[]
        {
            data.GetExactGamePath(gameBiz),
            data.GetGamePath(gameBiz),
        });
    }

    internal static IReadOnlyList<string> GetKnownGamePathCandidates(
        GameProfile profile,
        string server,
        IEnumerable<string?> configuredPaths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<string>();
        var candidates = configuredPaths.ToList();
        try
        {
            candidates.AddRange(GameSearchService.FindGames(profile, server, allowDifferentServer: true)
                .Select(x => x.Path));
        }
        catch { }

        // Multi-server hard-link installs are normally sibling directories.
        // Search only known parents so custom layouts are rediscovered without
        // an expensive or intrusive whole-drive scan.
        foreach (var seed in candidates.Where(x => !string.IsNullOrWhiteSpace(x)).ToList())
        {
            try
            {
                var parent = Directory.GetParent(Path.GetFullPath(seed!));
                if (parent?.Exists == true)
                    candidates.AddRange(parent.EnumerateDirectories().Select(x => x.FullName));
            }
            catch { }
        }

        foreach (var path in candidates)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            string normalized;
            try { normalized = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
            catch { continue; }
            if (seen.Add(normalized) && IsUsableGameDirectory(profile, normalized))
                results.Add(normalized);
        }
        return results;
    }

    internal static bool IsUsableGameDirectory(GameProfile profile, string path)
    {
        if (!Directory.Exists(path)) return false;
        return File.Exists(Path.Combine(path, profile.CnExeName))
            || File.Exists(Path.Combine(path, profile.GlobalExeName));
    }

    public static string GetDefaultInstallDir(DataModel data, string gameBiz)
    {
        var biz = new GameBiz(gameBiz);
        var game = biz.Game;
        var folder = game switch { "genshin" => "Genshin Impact", "starrail" => "Star Rail", "zzz" => "ZenlessZoneZero", "honkai3" => "Honkai Impact 3rd", _ => "miHoYo Game" };
        // Keep every server in a separate folder.  This is essential for
        // switching servers safely, while still allowing NTFS hard links to
        // share identical files between folders on the same drive.
        folder += $" ({biz.Server})";
        var drives = GetExistingGameDrives(data);
        if (drives.Count > 0) return Path.Combine(drives[0], "Program Files", "miHoYo Launcher", "games", folder);
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), folder);
    }

    /// <summary>
    /// Verifies that an install target does not point at another server's
    /// client.  Server clients may share files through hard links, never by
    /// sharing their root directory.
    /// </summary>
    public static bool TryValidateInstallTarget(DataModel data, string gameBiz, string installPath, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            error = "安装目录不能为空。";
            return false;
        }

        string normalized;
        try
        {
            normalized = Path.GetFullPath(installPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex)
        {
            error = $"安装目录无效：{ex.Message}";
            return false;
        }

        foreach (var profile in GameProfiles.All)
        {
            foreach (var server in new[] { "cn", "global", "bilibili" })
            {
                var otherBiz = $"{profile.Id}_{server}";
                if (string.Equals(otherBiz, gameBiz, StringComparison.OrdinalIgnoreCase))
                    continue;

                var configuredPath = data.GetExactGamePath(otherBiz);
                if (string.IsNullOrWhiteSpace(configuredPath))
                    continue;

                try
                {
                    var otherNormalized = Path.GetFullPath(configuredPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (string.Equals(normalized, otherNormalized, StringComparison.OrdinalIgnoreCase))
                    {
                        error = $"该目录已由 {otherBiz} 使用。不同服务器必须使用独立目录；安装器会通过硬链接复用资源。";
                        return false;
                    }
                }
                catch { }
            }
        }

        var targetBiz = new GameBiz(gameBiz);
        var detectedServer = GameSearchService.DetectServerFromConfig(normalized);
        if (!string.IsNullOrWhiteSpace(detectedServer) &&
            !string.Equals(detectedServer, targetBiz.Server, StringComparison.OrdinalIgnoreCase))
        {
            error = $"该目录包含 {detectedServer} 服客户端，不能直接转换为 {targetBiz.Server} 服。请选择一个新的空目录。";
            return false;
        }

        return true;
    }

    // ================================================
    //  Install
    // ================================================

    public async Task InstallGameAsync(string gameBiz, string installPath, CancellationToken ct = default)
    {
        BeginOperation(GameInstallOperation.Install, gameBiz, installPath);
        _cts?.Dispose();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _cts.Token;
        State = GameInstallState.Preparing;
        StatusText = "正在获取游戏版本信息...";
        ProgressPercent = 0;
        ErrorText = "";

        try
        {
            if (!TryValidateInstallTarget(_session.Data, gameBiz, installPath, out var installPathError))
            {
                State = GameInstallState.Error;
                ErrorText = installPathError ?? "安装目录不可用。";
                return;
            }

            Directory.CreateDirectory(installPath);
            string? hardLinkPath = FindHardLinkSource(gameBiz, installPath);
            var hardLinkAudioFields = GetInstalledAudioManifestFields(hardLinkPath);
            if (hardLinkAudioFields.Count > 0)
                Logger.Info($"Audio manifests selected for hard-link: {string.Join(", ", hardLinkAudioFields)}", "Install");
            else if (hardLinkPath != null)
                Logger.Info("Audio manifests selected for hard-link: all (audio directory scan found none)", "Install");

            // 1. Branch info
            var branch = await HoYoPlayApiService.GetGameBranchAsync(gameBiz, token);
            if (branch?.Main == null) { State = GameInstallState.Error; ErrorText = "无法获取版本信息"; return; }

            // 2. Resolve chunk plan (this downloads + parses manifest, ~20s)
            var plan = await HoYoPlayApiService.ResolveChunkDownloadPlanAsync(gameBiz, branch.Main, installPath, "",
                status => { StatusText = status; Logger.Debug(status, "Install"); }, token,
                includeAudioManifests: hardLinkPath != null,
                audioManifestFields: hardLinkAudioFields.Count > 0 ? hardLinkAudioFields : null);
            if (plan == null || plan.Files.Count == 0)
            {
                Logger.Warn("Chunk plan null, package fallback", "Install");
                await InstallViaPackageModeAsync(gameBiz, installPath, token);
                return;
            }

            StatusText = $"已解析 {plan.TotalFiles} 个文件, {plan.TotalChunks} 个分片";
            await Task.Yield(); // let UI repaint after manifest phase

            // ── Phase 1: Local match (size + MD5 when available) ──
            StatusText = "正在检查本地文件...";
            await Task.Yield();
            int localMatched = await MarkLocalFilesAsync(plan, installPath, token);
            Logger.Info($"Phase 1 - Local file match: {localMatched}/{plan.TotalFiles}", "Install");
            StatusText = $"本地文件匹配: {localMatched}/{plan.TotalFiles}";
            await Task.Yield();

            // ── Phase 2: Hard link (with MD5 verification, CPU-bound) ──
            int hardLinked = 0;
            if (hardLinkPath != null)
            {
                State = GameInstallState.Verifying;
                hardLinked = await HardLinkFilesAsync(plan, hardLinkPath, token);
                int hlTotal = plan.Files.Count(f => !f.IsFinished) + hardLinked;
                Logger.Info($"Hard-linked: {hardLinked}/{hlTotal} files", "Install");
                StatusText = $"硬链接完成: {hardLinked}/{hlTotal} 个文件";
            }

            // ── Phase 3: Calculate remaining ──
            var audioGroups = plan.Files.Where(f => !f.IsFinished).GroupBy(f => f.ManifestField);
            foreach (var g in audioGroups)
            {
                long bytes = g.Sum(f => f.Chunks.Sum(c => c.CompressedSize));
                Logger.Info($"Remaining [{g.Key}]: {g.Count()} files, {FormatBytes(bytes)}", "Install");
            }

            var remainingFiles = plan.Files.Where(f => !f.IsFinished).ToList();
            var deferredAudioFiles = remainingFiles.Where(f => HoYoPlayApiService.IsAudioManifest(f.ManifestField)).ToList();
            if (deferredAudioFiles.Count > 0)
            {
                long deferredBytes = deferredAudioFiles.Sum(f => f.Chunks.Sum(c => c.CompressedSize));
                Logger.Info($"Deferred audio files after hard-link: {deferredAudioFiles.Count} files, {FormatBytes(deferredBytes)}", "Install");
            }

            var toDownload = remainingFiles.Where(f => !HoYoPlayApiService.IsAudioManifest(f.ManifestField)).ToList();
            long remainingBytes = toDownload.Sum(f => f.Chunks.Sum(c => c.CompressedSize));
            int skippedCount = localMatched + hardLinked;

            if (toDownload.Count == 0)
            {
                State = GameInstallState.Verifying; StatusText = "正在写入配置...";
                var sdkVersion = await DownloadGameChannelSdkAsync(gameBiz, installPath, token);
                SetGameConfigIni(gameBiz, installPath, plan.Version, sdkVersion);
                State = GameInstallState.Finished;
                StatusText = deferredAudioFiles.Count > 0 ? "安装完成，部分语音包可在设置中补装" : "安装完成";
                ProgressPercent = 100;
                Logger.Info($"Install complete (all skipped): local={localMatched} hardlink={hardLinked}", "Install");
                return;
            }

            // ── Phase 4: Download ──
            State = GameInstallState.Downloading;
            _totalBytes = remainingBytes;
            _downloadedBytes = 0;
            StatusText = $"正在下载 {toDownload.Count} 个文件 ({FormatBytes(remainingBytes)})...";
            Logger.Info($"Download phase: {toDownload.Count} files, {FormatBytes(remainingBytes)}", "Install");
            StartSpeedTracking();

            int dlDone = 0;
            await RunParallelWithCancelAsync(toDownload, token, async (file, ct2) =>
            {
                var httpClient = _httpClients[Environment.CurrentManagedThreadId % _httpClients.Length];
                await DownloadChunksToFileAsync(httpClient, file, ct2);
                file.IsFinished = true;
                Interlocked.Increment(ref dlDone);
            });

            if (token.IsCancellationRequested) { State = GameInstallState.Paused; StatusText = "已暂停"; StopSpeedTracking(); return; }

            // ── Phase 5: Config ──
            StopSpeedTracking();
            State = GameInstallState.Verifying; StatusText = "正在写入配置...";
            var finalSdkVersion = await DownloadGameChannelSdkAsync(gameBiz, installPath, token);
            SetGameConfigIni(gameBiz, installPath, plan.Version, finalSdkVersion);
            CleanupTempFiles(installPath);
            State = GameInstallState.Finished;
            StatusText = deferredAudioFiles.Count > 0 ? "安装完成，部分语音包可在设置中补装" : "安装完成";
            ProgressPercent = 100;
            Logger.Info($"Install: local={localMatched} hardlink={hardLinked} downloaded={dlDone} total={plan.TotalFiles}", "Install");
        }
        catch (OperationCanceledException) { State = GameInstallState.Paused; StatusText = "已暂停"; StopSpeedTracking(); }
        catch (Exception ex) { Logger.Error($"Install failed: {ex}", "Install"); State = GameInstallState.Error; ErrorText = ex.Message; StopSpeedTracking(); }
    }

    // ================================================
    //  Update
    // ================================================

    public async Task UpdateGameAsync(string gameBiz, string installPath, CancellationToken ct = default)
    {
        BeginOperation(GameInstallOperation.Update, gameBiz, installPath);
        _cts?.Dispose();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _cts.Token;
        State = GameInstallState.Preparing;
        StatusText = "正在检查更新...";
        ErrorText = "";

        try
        {
            var localVersion = GameStateService.GetLocalVersion(installPath);
            var branch = await HoYoPlayApiService.GetGameBranchAsync(gameBiz, token);
            if (branch?.Main == null) { State = GameInstallState.Error; ErrorText = "无法获取版本信息"; return; }

            var plan = await HoYoPlayApiService.ResolveChunkDownloadPlanAsync(gameBiz, branch.Main, installPath, localVersion?.ToString() ?? "",
                status => { StatusText = status; Logger.Debug(status, "Update"); }, token);
            if (plan == null || plan.Files.Count == 0) { State = GameInstallState.Finished; StatusText = "已是最新"; ProgressPercent = 100; return; }

            StatusText = $"已解析 {plan.TotalFiles} 个文件";
            await Task.Yield(); // let UI repaint
            int localMatched = await MarkLocalFilesAsync(plan, installPath, token);
            Logger.Info($"Update: {localMatched}/{plan.TotalFiles} files unchanged", "Install");
            StatusText = $"本地文件匹配: {localMatched}/{plan.TotalFiles}";
            await Task.Yield();

            var toDownload = plan.Files.Where(f => !f.IsFinished).ToList();
            long remainingBytes = toDownload.Sum(f => f.Chunks.Sum(c => c.CompressedSize));

            if (toDownload.Count == 0)
            {
                var sdkVersion = await DownloadGameChannelSdkAsync(gameBiz, installPath, token);
                SetGameConfigIni(gameBiz, installPath, plan.Version, sdkVersion);
                CleanupTempFiles(installPath);
                State = GameInstallState.Finished;
                StatusText = "已是最新";
                ProgressPercent = 100;
                return;
            }

            State = GameInstallState.Downloading;
            _totalBytes = remainingBytes; _downloadedBytes = 0;
            StatusText = $"正在更新 {toDownload.Count} 个文件 ({FormatBytes(remainingBytes)})...";
            StartSpeedTracking();

            int dlDone = 0;
            await RunParallelWithCancelAsync(toDownload, token, async (file, ct2) =>
            {
                var httpClient = _httpClients[Environment.CurrentManagedThreadId % _httpClients.Length];
                await DownloadChunksToFileAsync(httpClient, file, ct2);
                file.IsFinished = true;
                Interlocked.Increment(ref dlDone);
            });

            if (token.IsCancellationRequested) { State = GameInstallState.Paused; StatusText = "已暂停"; StopSpeedTracking(); return; }

            StopSpeedTracking();
            SetGameConfigIni(gameBiz, installPath, plan.Version);
            CleanupTempFiles(installPath);
            State = GameInstallState.Finished; StatusText = "更新完成"; ProgressPercent = 100;
        }
        catch (OperationCanceledException) { State = GameInstallState.Paused; StatusText = "已暂停"; StopSpeedTracking(); }
        catch (Exception ex) { Logger.Error($"Update failed: {ex}", "Install"); State = GameInstallState.Error; ErrorText = ex.Message; StopSpeedTracking(); }
    }

    // ================================================
    //  PreDownload
    // ================================================

    public async Task PreDownloadAsync(string gameBiz, string installPath, CancellationToken ct = default)
    {
        BeginOperation(GameInstallOperation.PreDownload, gameBiz, installPath);
        _cts?.Dispose();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _cts.Token;
        State = GameInstallState.Downloading;
        StatusText = "正在获取预下载信息...";
        ErrorText = "";

        try
        {
            var branch = await HoYoPlayApiService.GetGameBranchAsync(gameBiz, token);
            if (branch?.PreDownload == null) { State = GameInstallState.Error; ErrorText = "无可预下载内容"; return; }

            var localVersion = GameStateService.GetLocalVersion(installPath);
            var audioManifestFields = GetInstalledAudioManifestFields(installPath);
            var plan = await HoYoPlayApiService.ResolveChunkDownloadPlanAsync(gameBiz, branch.PreDownload, installPath, localVersion?.ToString() ?? "",
                status => { StatusText = status; Logger.Debug(status, "PreDownload"); }, token,
                includeAudioManifests: audioManifestFields.Count > 0,
                audioManifestFields: audioManifestFields.Count > 0 ? audioManifestFields : null);
            if (plan == null || plan.Files.Count == 0) { State = GameInstallState.Finished; StatusText = "无预下载"; return; }

            var chunks = BuildPredownloadChunkList(plan, installPath);
            var toDownload = chunks.Where(x => !IsChunkCacheReady(x.Chunk, x.CachePath)).ToList();
            long remainingBytes = toDownload.Sum(x => x.Chunk.CompressedSize);

            if (toDownload.Count == 0)
            {
                MarkPredownload(gameBiz, installPath, localVersion?.ToString(), branch.PreDownload.Tag, audioManifestFields);
                State = GameInstallState.Finished;
                StatusText = "预下载完成";
                ProgressPercent = 100;
                return;
            }

            _totalBytes = remainingBytes; _downloadedBytes = 0;
            StartSpeedTracking();
            StatusText = $"预下载中 ({toDownload.Count} 个分片, {FormatBytes(remainingBytes)})...";

            int dlDone = 0;
            await RunParallelWithCancelAsync(toDownload, token, async (file, ct2) =>
            {
                var httpClient = _httpClients[Environment.CurrentManagedThreadId % _httpClients.Length];
                await DownloadChunkCacheAsync(httpClient, file.Chunk, file.CachePath, ct2);
                Interlocked.Increment(ref dlDone);
            });

            if (token.IsCancellationRequested) { State = GameInstallState.Paused; StatusText = "已暂停"; StopSpeedTracking(); return; }

            MarkPredownload(gameBiz, installPath, localVersion?.ToString(), branch.PreDownload.Tag, audioManifestFields);
            StopSpeedTracking();
            State = GameInstallState.Finished; StatusText = "预下载完成"; ProgressPercent = 100;
        }
        catch (OperationCanceledException) { State = GameInstallState.Paused; StatusText = "已暂停"; StopSpeedTracking(); }
        catch (Exception ex) { Logger.Error($"Predownload failed: {ex}", "Install"); State = GameInstallState.Error; ErrorText = ex.Message; StopSpeedTracking(); }
    }

    // ================================================
    //  Verify / Repair
    // ================================================

    public async Task<List<GameResourceIssue>> VerifyGameResourcesAsync(string gameBiz, string installPath, CancellationToken ct = default)
    {
        BeginOperation(GameInstallOperation.Verify, gameBiz, installPath);
        _cts?.Dispose();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _cts.Token;
        State = GameInstallState.Verifying;
        StatusText = "正在获取资源清单...";
        ProgressPercent = 0;
        ErrorText = "";

        try
        {
            var localVersion = GameStateService.GetLocalVersion(installPath);
            var branch = await HoYoPlayApiService.GetGameBranchAsync(gameBiz, token);
            if (branch?.Main == null)
                throw new InvalidOperationException("无法获取版本信息");

            var audioManifestFields = GetInstalledAudioManifestFields(installPath);
            var plan = await HoYoPlayApiService.ResolveChunkDownloadPlanAsync(gameBiz, branch.Main, installPath,
                localVersion?.ToString() ?? "",
                status => { StatusText = status; Logger.Debug(status, "Verify"); }, token,
                includeAudioManifests: audioManifestFields.Count > 0,
                audioManifestFields: audioManifestFields.Count > 0 ? audioManifestFields : null);

            if (plan == null)
                throw new InvalidOperationException("无法获取资源清单");

            var verification = await VerifyPlanFilesAsync(plan, installPath, token);
            LastVerification = verification;
            var issues = verification.Issues;

            State = GameInstallState.Finished;
            StatusText = issues.Count == 0 ? "资源校验完成，未发现问题" : $"资源校验完成，发现 {issues.Count} 个异常文件";
            ProgressPercent = 100;
            return issues;
        }
        catch (OperationCanceledException)
        {
            State = GameInstallState.Paused;
            StatusText = "已暂停";
            return new List<GameResourceIssue>();
        }
        catch (Exception ex)
        {
            Logger.Error($"Verify failed: {ex}", "Install");
            State = GameInstallState.Error;
            ErrorText = ex.Message;
            return new List<GameResourceIssue>();
        }
    }

    public async Task RepairGameAsync(string gameBiz, string installPath, CancellationToken ct = default)
    {
        BeginOperation(GameInstallOperation.Repair, gameBiz, installPath);
        _cts?.Dispose();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _cts.Token;
        State = GameInstallState.Preparing;
        StatusText = "正在准备资源修复...";
        ProgressPercent = 0;
        ErrorText = "";

        try
        {
            var localVersion = GameStateService.GetLocalVersion(installPath);
            var branch = await HoYoPlayApiService.GetGameBranchAsync(gameBiz, token);
            if (branch?.Main == null) { State = GameInstallState.Error; ErrorText = "无法获取版本信息"; return; }

            var audioManifestFields = GetInstalledAudioManifestFields(installPath);
            var plan = await HoYoPlayApiService.ResolveChunkDownloadPlanAsync(gameBiz, branch.Main, installPath,
                localVersion?.ToString() ?? "",
                status => { StatusText = status; Logger.Debug(status, "Repair"); }, token,
                includeAudioManifests: audioManifestFields.Count > 0,
                audioManifestFields: audioManifestFields.Count > 0 ? audioManifestFields : null);
            if (plan == null || plan.Files.Count == 0) { State = GameInstallState.Error; ErrorText = "无法获取资源清单"; return; }

            State = GameInstallState.Verifying;
            int matched = await MarkLocalFilesAsync(plan, installPath, token);
            Logger.Info($"Repair: {matched}/{plan.TotalFiles} files passed verification", "Install");

            var toDownload = plan.Files.Where(f => !f.IsFinished).ToList();
            if (toDownload.Count == 0)
            {
                var sdkVersion = await DownloadGameChannelSdkAsync(gameBiz, installPath, token);
                SetGameConfigIni(gameBiz, installPath, plan.Version, sdkVersion);
                State = GameInstallState.Finished;
                StatusText = "资源完整，无需修复";
                ProgressPercent = 100;
                return;
            }

            long remainingBytes = toDownload.Sum(f => f.Chunks.Sum(c => c.CompressedSize));
            State = GameInstallState.Downloading;
            _totalBytes = remainingBytes;
            _downloadedBytes = 0;
            StatusText = $"正在修复 {toDownload.Count} 个文件 ({FormatBytes(remainingBytes)})...";
            StartSpeedTracking();

            int dlDone = 0;
            await RunParallelWithCancelAsync(toDownload, token, async (file, ct2) =>
            {
                var httpClient = _httpClients[Environment.CurrentManagedThreadId % _httpClients.Length];
                await DownloadChunksToFileAsync(httpClient, file, ct2);
                file.IsFinished = true;
                Interlocked.Increment(ref dlDone);
            });

            if (token.IsCancellationRequested) { State = GameInstallState.Paused; StatusText = "已暂停"; StopSpeedTracking(); return; }

            StopSpeedTracking();
            var finalSdkVersion = await DownloadGameChannelSdkAsync(gameBiz, installPath, token);
            SetGameConfigIni(gameBiz, installPath, plan.Version, finalSdkVersion);
            CleanupTempFiles(installPath);
            State = GameInstallState.Finished;
            StatusText = "资源修复完成";
            ProgressPercent = 100;
        }
        catch (OperationCanceledException) { State = GameInstallState.Paused; StatusText = "已暂停"; StopSpeedTracking(); }
        catch (Exception ex) { Logger.Error($"Repair failed: {ex}", "Install"); State = GameInstallState.Error; ErrorText = ex.Message; StopSpeedTracking(); }
    }

    public void Pause()
    {
        if (!CanPause) return;
        try { _cts?.Cancel(); } catch { }
    }

    /// <summary>
    /// Starts the most recently paused or failed operation again. Downloaded
    /// chunks and completed files are retained, so this is a true continuation
    /// for chunk downloads and pre-download caches.
    /// </summary>
    public Task ContinueAsync(CancellationToken ct = default)
    {
        if (!CanContinue || Operation == null || string.IsNullOrWhiteSpace(ActiveGameBiz) || string.IsNullOrWhiteSpace(ActiveInstallPath))
            return Task.CompletedTask;

        return Operation.Value switch
        {
            GameInstallOperation.Install => InstallGameAsync(ActiveGameBiz, ActiveInstallPath, ct),
            GameInstallOperation.Update => UpdateGameAsync(ActiveGameBiz, ActiveInstallPath, ct),
            GameInstallOperation.PreDownload => PreDownloadAsync(ActiveGameBiz, ActiveInstallPath, ct),
            GameInstallOperation.Verify => VerifyGameResourcesAsync(ActiveGameBiz, ActiveInstallPath, ct),
            GameInstallOperation.Repair => RepairGameAsync(ActiveGameBiz, ActiveInstallPath, ct),
            _ => Task.CompletedTask,
        };
    }

    // ================================================
    //  Parallel runner (cancellation-safe)
    // ================================================

    private async Task RunParallelWithCancelAsync<T>(IList<T> items, CancellationToken ct, Func<T, CancellationToken, Task> action)
    {
        using var semaphore = new SemaphoreSlim(_maxParallelism, _maxParallelism);
        var errors = new ConcurrentQueue<Exception>();
        var tasks = items.Select(async item =>
        {
            if (ct.IsCancellationRequested) return;
            await semaphore.WaitAsync(CancellationToken.None);
            try { if (!ct.IsCancellationRequested) await action(item, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex) { Logger.Warn($"Task error: {ex.Message}", "Install"); errors.Enqueue(ex); }
            finally { semaphore.Release(); }
        }).ToArray();
        await Task.WhenAll(tasks);
        if (errors.TryPeek(out var firstError))
            throw new InvalidOperationException($"Parallel task failed: {firstError.Message}", firstError);
    }

    // ================================================
    //  Local file matching
    // ================================================

    /// <summary>
    /// Mark files already present in the install directory by comparing path, size and MD5 when available.
    /// </summary>
    private async Task<int> MarkLocalFilesAsync(ChunkDownloadPlan plan, string installPath, CancellationToken ct)
    {
        var candidates = plan.Files
            .Where(file =>
            {
                if (file.IsFinished) return false;
                var localPath = ResolveSafeChildPath(installPath, file.RelativePath);
                return localPath != null && File.Exists(localPath) && new FileInfo(localPath).Length == file.Size;
            })
            .ToList();
        if (candidates.Count == 0) return 0;

        int matched = 0, done = 0, total = candidates.Count;
        int degree = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
        StatusText = $"正在校验本地文件 ({total})...";

        await Task.Run(() =>
        {
            Parallel.ForEach(candidates, new ParallelOptions { MaxDegreeOfParallelism = degree, CancellationToken = ct }, file =>
            {
                ct.ThrowIfCancellationRequested();
                var localPath = ResolveSafeChildPath(installPath, file.RelativePath);
                if (localPath != null && (string.IsNullOrWhiteSpace(file.MD5) || VerifyFileMD5(localPath, file.MD5)))
                {
                    file.IsFinished = true;
                    Interlocked.Increment(ref matched);
                }

                var current = Interlocked.Increment(ref done);
                if (current % 100 == 0 || current == total)
                    ProgressPercent = (double)current / total * 100;
            });
        }, ct);

        return matched;
    }

    /// <summary>
    /// Verifies every manifest file independently.  Unlike the download fast
    /// path, this never trusts a size match: when a manifest provides an MD5 it
    /// is always checked, so callers can report missing, size-invalid and
    /// hash-invalid files separately.
    /// </summary>
    private async Task<GameResourceVerificationResult> VerifyPlanFilesAsync(ChunkDownloadPlan plan, string installPath, CancellationToken ct)
    {
        var result = new GameResourceVerificationResult { TotalFiles = plan.Files.Count };
        var issues = new ConcurrentBag<GameResourceIssue>();
        int valid = 0, missing = 0, invalid = 0, done = 0;
        int degree = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
        StatusText = $"正在校验 {plan.TotalFiles} 个文件...";

        await Task.Run(() =>
        {
            Parallel.ForEach(plan.Files, new ParallelOptions { MaxDegreeOfParallelism = degree, CancellationToken = ct }, file =>
            {
                ct.ThrowIfCancellationRequested();
                var localPath = ResolveSafeChildPath(installPath, file.RelativePath);
                GameResourceIssue? issue = null;
                if (localPath == null || !File.Exists(localPath))
                {
                    issue = CreateResourceIssue(file, "缺失");
                    Interlocked.Increment(ref missing);
                }
                else if (new FileInfo(localPath).Length != file.Size)
                {
                    issue = CreateResourceIssue(file, "文件大小不匹配");
                    Interlocked.Increment(ref invalid);
                }
                else if (!string.IsNullOrWhiteSpace(file.MD5) && !VerifyFileMD5(localPath, file.MD5))
                {
                    issue = CreateResourceIssue(file, "MD5 校验失败");
                    Interlocked.Increment(ref invalid);
                }
                else
                {
                    file.IsFinished = true;
                    Interlocked.Increment(ref valid);
                }

                if (issue != null) issues.Add(issue);
                var current = Interlocked.Increment(ref done);
                if (current % 50 == 0 || current == plan.TotalFiles)
                {
                    ProgressPercent = plan.TotalFiles == 0 ? 100 : (double)current / plan.TotalFiles * 100;
                    StatusText = $"正在校验文件 ({current}/{plan.TotalFiles})...";
                }
            });
        }, ct);

        result.ValidFiles = valid;
        result.MissingFiles = missing;
        result.InvalidFiles = invalid;
        result.Issues = issues.OrderBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
        return result;
    }

    private static GameResourceIssue CreateResourceIssue(ChunkDownloadFile file, string reason) => new()
    {
        RelativePath = file.RelativePath,
        Size = file.Size,
        MD5 = file.MD5,
        ManifestField = file.ManifestField,
        Reason = reason,
    };

    /// <summary>
    /// Files in high-risk directories where size-only matching is insufficient.
    /// The game client validates these on launch and triggers re-download if content differs.
    /// </summary>
    private static bool NeedsMD5Verification(string relativePath)
    {
        // Persistent folder: resource block files (.blk) that the game client checks
        if (relativePath.Contains("Persistent", StringComparison.OrdinalIgnoreCase))
            return true;
        // StreamingAssets .blk files: resource blocks
        if (relativePath.Contains("StreamingAssets", StringComparison.OrdinalIgnoreCase)
            && relativePath.EndsWith(".blk", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    /// <summary>
    /// Verify MD5 for local files that were matched by size but are in critical directories.
    /// Files that fail verification are unmarked so they will be re-downloaded.
    /// Follows Starward's pattern: check size first, then MD5 for matching files.
    /// </summary>
    private async Task<int> VerifyLocalFileMD5Async(ChunkDownloadPlan plan, string installPath, CancellationToken ct)
    {
        var toVerify = plan.Files
            .Where(f => f.IsFinished && !string.IsNullOrEmpty(f.MD5) && NeedsMD5Verification(f.RelativePath))
            .ToList();
        if (toVerify.Count == 0) return 0;

        int failed = 0, done = 0, total = toVerify.Count;
        StatusText = $"正在校验关键文件 ({total})...";

        await Task.Run(() =>
        {
            Parallel.ForEach(toVerify, new ParallelOptions { MaxDegreeOfParallelism = _maxParallelism, CancellationToken = ct }, file =>
            {
                ct.ThrowIfCancellationRequested();
                var localPath = ResolveSafeChildPath(installPath, file.RelativePath);
                if (localPath == null)
                {
                    file.IsFinished = false;
                    Interlocked.Increment(ref failed);
                    Logger.Warn($"Unsafe local path, will re-download: {file.RelativePath}", "Install");
                    return;
                }
                if (!VerifyFileMD5(localPath, file.MD5))
                {
                    file.IsFinished = false;
                    Interlocked.Increment(ref failed);
                    Logger.Warn($"MD5 mismatch, will re-download: {file.RelativePath}", "Install");
                }
                var current = Interlocked.Increment(ref done);
                if (current % 100 == 0 || current == total)
                    ProgressPercent = (double)current / total * 100;
            });
        }, ct);

        Logger.Info($"MD5 verify: {done} checked, {failed} failed (will re-download)", "Install");
        return failed;
    }

    /// <summary>
    /// Try to hard-link files from another game installation. Every candidate
    /// that has a manifest checksum is verified before it is linked; equal
    /// version strings and file sizes are not proof of identical cross-server
    /// content.
    /// </summary>
    private async Task<int> HardLinkFilesAsync(ChunkDownloadPlan plan, string hardLinkSourcePath, CancellationToken ct)
    {
        PrepareHardLinkTargets(plan, hardLinkSourcePath);

        var toLink = plan.Files
            .Where(f => !f.IsFinished && !string.IsNullOrWhiteSpace(f.HardLinkTarget))
            .ToList();
        if (toLink.Count == 0)
            return 0;

        int linked = 0, done = 0, total = toLink.Count;
        int degree = Math.Clamp(Environment.ProcessorCount, 4, _maxParallelism);

        Logger.Info($"Hard link prepared: candidates={total}, targetVersion={plan.Version}, checksumVerification=true", "Install");
        StatusText = $"正在创建硬链接 ({total})...";

        await Task.Run(() =>
        {
            Parallel.ForEach(toLink, new ParallelOptions { MaxDegreeOfParallelism = degree, CancellationToken = ct }, file =>
            {
                ct.ThrowIfCancellationRequested();
                if (TryHardLinkPrepared(file))
                    Interlocked.Increment(ref linked);

                var current = Interlocked.Increment(ref done);
                if (current % 100 == 0 || current == total)
                {
                    ProgressPercent = (double)current / total * 100;
                    StatusText = $"正在创建硬链接 ({current}/{total}, 成功 {Volatile.Read(ref linked)})...";
                }
            });
        }, ct);

        return linked;
    }

    private void PrepareHardLinkTargets(ChunkDownloadPlan plan, string hardLinkSourcePath)
    {
        string? sourceGenshinDataFolder = null;
        if (new GameBiz(plan.GameBiz).Game == "genshin")
            sourceGenshinDataFolder = GetExistingGenshinDataFolder(hardLinkSourcePath);

        var targetGenshinDataFolder = GetExpectedGenshinDataFolder(plan.GameBiz);
        foreach (var file in plan.Files.Where(f => !f.IsFinished))
        {
            var rel = file.RelativePath;
            if (!string.IsNullOrWhiteSpace(sourceGenshinDataFolder) &&
                !string.IsNullOrWhiteSpace(targetGenshinDataFolder) &&
                !string.Equals(sourceGenshinDataFolder, targetGenshinDataFolder, StringComparison.OrdinalIgnoreCase))
            {
                rel = ReplacePathSegment(rel, targetGenshinDataFolder, sourceGenshinDataFolder);
            }

            file.HardLinkTarget = ResolveSafeChildPath(hardLinkSourcePath, rel) ?? "";
        }
    }

    private static string? GetExpectedGenshinDataFolder(string gameBiz)
    {
        if (new GameBiz(gameBiz).Game != "genshin")
            return null;
        return new GameBiz(gameBiz).IsGlobalServer() ? "GenshinImpact_Data" : "YuanShen_Data";
    }

    private static string? GetExistingGenshinDataFolder(string hardLinkSourcePath)
    {
        try
        {
            foreach (var name in new[] { "YuanShen_Data", "GenshinImpact_Data" })
            {
                if (Directory.Exists(Path.Combine(hardLinkSourcePath, name)))
                    return name;
            }
        }
        catch { }
        return null;
    }

    private static string ReplacePathSegment(string relativePath, string oldSegment, string newSegment)
    {
        if (relativePath.Equals(oldSegment, StringComparison.OrdinalIgnoreCase))
            return newSegment;

        var prefix = oldSegment + Path.DirectorySeparatorChar;
        if (relativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return newSegment + relativePath[oldSegment.Length..];

        var altPrefix = oldSegment + Path.AltDirectorySeparatorChar;
        if (relativePath.StartsWith(altPrefix, StringComparison.OrdinalIgnoreCase))
            return newSegment + relativePath[oldSegment.Length..];

        return relativePath;
    }

    private bool TryHardLinkPrepared(ChunkDownloadFile file)
    {
        if (file.IsFinished || string.IsNullOrWhiteSpace(file.HardLinkTarget))
            return false;

        return TryHardLinkMatchSize(file, file.HardLinkTarget, verifyMD5: true);
    }

    private bool TryHardLinkBySize(ChunkDownloadFile file, string hardLinkSourcePath)
    {
        if (file.IsFinished) return false;

        // Try exact same relative path
        var target = ResolveSafeChildPath(hardLinkSourcePath, file.RelativePath);
        if (target == null) return false;
        if (TryHardLinkMatchSize(file, target, verifyMD5: true))
            return true;

        // For Genshin: try YuanShen_Data <-> GenshinImpact_Data swap
        var rel = file.RelativePath;
        if (rel.Contains("YuanShen_Data"))
        {
            target = ResolveSafeChildPath(hardLinkSourcePath, rel.Replace("YuanShen_Data", "GenshinImpact_Data"));
            if (target == null) return false;
            if (TryHardLinkMatchSize(file, target, verifyMD5: true)) return true;
        }
        else if (rel.Contains("GenshinImpact_Data"))
        {
            target = ResolveSafeChildPath(hardLinkSourcePath, rel.Replace("GenshinImpact_Data", "YuanShen_Data"));
            if (target == null) return false;
            if (TryHardLinkMatchSize(file, target, verifyMD5: true)) return true;
        }

        return false;
    }

    private bool TryHardLinkMatchSize(ChunkDownloadFile file, string sourcePath, bool verifyMD5)
    {
        if (!File.Exists(sourcePath)) return false;
        var sourceLen = new FileInfo(sourcePath).Length;
        if (sourceLen != file.Size) return false;
        try
        {
            if (verifyMD5 && !string.IsNullOrEmpty(file.MD5))
            {
                if (!VerifyFileMD5(sourcePath, file.MD5))
                    return false;
            }
            if (TryCreateHardLink(file.FullPath, sourcePath))
            {
                file.IsFinished = true;
                file.HardLinkTarget = sourcePath;
                return true;
            }
        }
        catch { }
        return false;
    }

    private static bool VerifyFileMD5(string path, string expectedMD5)
    {
        try
        {
            using var md5 = MD5.Create();
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, MD5_BUFFER_SIZE, FileOptions.SequentialScan);
            var buffer = new byte[MD5_BUFFER_SIZE];
            int read;
            while ((read = fs.Read(buffer)) > 0)
                md5.TransformBlock(buffer, 0, read, null, 0);
            md5.TransformFinalBlock(buffer, 0, 0);
            if (md5.Hash == null) return false;
            return string.Equals(Convert.ToHexString(md5.Hash), expectedMD5, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    // ================================================
    //  Chunk download with retry
    // ================================================

    private async Task DownloadChunksToFileAsync(HttpClient httpClient, ChunkDownloadFile file, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(file.FullPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        if (File.Exists(file.FullPath) &&
            new FileInfo(file.FullPath).Length == file.Size &&
            (string.IsNullOrWhiteSpace(file.MD5) || VerifyFileMD5(file.FullPath, file.MD5)))
        {
            foreach (var chunk in file.Chunks)
                ReportBytes(chunk.CompressedSize);
            file.IsFinished = true;
            return;
        }

        var tmpPath = file.FullPath + "_tmp";
        await using var fs = new FileStream(tmpPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);

        foreach (var chunk in file.Chunks)
        {
            ct.ThrowIfCancellationRequested();

            // Resume: skip already-written chunks
            if (fs.Length >= chunk.Offset + chunk.UncompressedSize)
            {
                ReportBytes(chunk.CompressedSize);
                continue;
            }

            fs.Position = chunk.Offset;

            if (await TryCopyOriginalChunkAsync(fs, chunk, ct))
            {
                ReportBytes(chunk.CompressedSize);
                continue;
            }

            if (await TryCopyCachedChunkAsync(fs, file, chunk, ct))
            {
                ReportBytes(chunk.CompressedSize);
                continue;
            }

            const int maxRetries = 5;
            for (int retry = 0; ; retry++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var response = await httpClient.GetAsync(chunk.Url, HttpCompletionOption.ResponseHeadersRead, ct);
                    response.EnsureSuccessStatusCode();
                    using var networkStream = await response.Content.ReadAsStreamAsync(ct);
                    using var decompressor = new DecompressionStream(networkStream);
                    var buffer = new byte[65536];
                    int read;
                    while ((read = await decompressor.ReadAsync(buffer, ct)) > 0)
                    {
                        await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                    }
                    // Report compressed size (network bytes), not decompressed size
                    ReportBytes(chunk.CompressedSize);
                    break;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    if (retry >= maxRetries) { Logger.Error($"Chunk failed: {chunk.Id}", "Install"); throw; }
                    int delay = Math.Min(1000 * (1 << retry), 16000);
                    Logger.Debug($"Retry {retry + 1}: {chunk.Id} in {delay}ms ({ex.Message})", "Install");
                    try { await Task.Delay(delay, ct); } catch (OperationCanceledException) { throw; }
                }
            }
        }

        await fs.FlushAsync(ct);
        await fs.DisposeAsync();

        if (File.Exists(tmpPath) && new FileInfo(tmpPath).Length == file.Size)
        {
            if (!string.IsNullOrEmpty(file.MD5) && !VerifyFileMD5(tmpPath, file.MD5))
            {
                File.Delete(tmpPath);
                throw new IOException($"MD5 mismatch: {file.RelativePath}");
            }
            File.Move(tmpPath, file.FullPath, true);
        }
        else if (File.Exists(tmpPath))
        {
            var actual = new FileInfo(tmpPath).Length;
            File.Delete(tmpPath);
            throw new IOException($"Size mismatch: {file.RelativePath} expected={file.Size} actual={actual}");
        }
    }

    private static string? GetChunkCachePath(string? installPath, string chunkId)
    {
        if (string.IsNullOrWhiteSpace(installPath) || string.IsNullOrWhiteSpace(chunkId))
            return null;
        var chunkDir = Path.Combine(installPath, "chunk");
        return ResolveSafeChildPath(chunkDir, chunkId);
    }

    private async Task<bool> TryCopyOriginalChunkAsync(FileStream destination, ChunkInfo chunk, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(chunk.OriginalFileFullPath) ||
                !File.Exists(chunk.OriginalFileFullPath) ||
                new FileInfo(chunk.OriginalFileFullPath).Length != chunk.OriginalFileSize)
            {
                return false;
            }

            if (!await VerifyFileSliceMD5Async(chunk.OriginalFileFullPath, chunk.OriginalFileOffset,
                    chunk.UncompressedSize, chunk.UncompressedMD5, ct))
            {
                return false;
            }

            await CopyFileSliceAsync(chunk.OriginalFileFullPath, destination, chunk.OriginalFileOffset,
                chunk.UncompressedSize, ct);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Logger.Debug($"Copy original chunk failed: {chunk.Id} ({ex.Message})", "Install");
            return false;
        }
    }

    private async Task<bool> TryCopyCachedChunkAsync(FileStream destination, ChunkDownloadFile file, ChunkInfo chunk, CancellationToken ct)
    {
        var cachePath = GetChunkCachePath(file.InstallPath, chunk.Id);
        if (cachePath == null || !File.Exists(cachePath))
            return false;

        try
        {
            if (new FileInfo(cachePath).Length != chunk.CompressedSize ||
                (!string.IsNullOrWhiteSpace(chunk.CompressedMD5) && !VerifyFileMD5(cachePath, chunk.CompressedMD5)))
            {
                TryDeleteFile(cachePath);
                return false;
            }

            await using var cache = new FileStream(cachePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var decompressor = new DecompressionStream(cache);
            await decompressor.CopyToAsync(destination, ct);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Logger.Debug($"Copy cached chunk failed: {chunk.Id} ({ex.Message})", "Install");
            TryDeleteFile(cachePath);
            return false;
        }
    }

    private static async Task<bool> VerifyFileSliceMD5Async(string path, long offset, long length, string expectedMD5, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(expectedMD5))
            return false;

        var info = new FileInfo(path);
        if (!info.Exists || info.Length < offset + length)
            return false;

        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            MD5_BUFFER_SIZE, FileOptions.SequentialScan);
        fs.Position = offset;
        using var md5 = MD5.Create();
        var buffer = new byte[MD5_BUFFER_SIZE];
        long remaining = length;
        while (remaining > 0)
        {
            int read = await fs.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), ct);
            if (read == 0) break;
            md5.TransformBlock(buffer, 0, read, null, 0);
            remaining -= read;
        }
        md5.TransformFinalBlock(buffer, 0, 0);
        return remaining == 0 &&
               md5.Hash != null &&
               string.Equals(Convert.ToHexString(md5.Hash), expectedMD5, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task CopyFileSliceAsync(string sourcePath, FileStream destination, long offset, long length, CancellationToken ct)
    {
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            65536, FileOptions.SequentialScan);
        source.Position = offset;
        var buffer = new byte[65536];
        long remaining = length;
        while (remaining > 0)
        {
            int read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), ct);
            if (read == 0) throw new EndOfStreamException(sourcePath);
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
            remaining -= read;
        }
    }

    private sealed class PredownloadChunkItem
    {
        public ChunkInfo Chunk { get; init; } = null!;
        public string CachePath { get; init; } = "";
    }

    private List<PredownloadChunkItem> BuildPredownloadChunkList(ChunkDownloadPlan plan, string installPath)
    {
        var result = new Dictionary<string, PredownloadChunkItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in plan.Files)
        {
            foreach (var chunk in file.Chunks)
            {
                if (!string.IsNullOrWhiteSpace(chunk.OriginalFileFullPath) &&
                    File.Exists(chunk.OriginalFileFullPath) &&
                    new FileInfo(chunk.OriginalFileFullPath).Length == chunk.OriginalFileSize)
                {
                    continue;
                }

                var cachePath = GetChunkCachePath(installPath, chunk.Id);
                if (cachePath == null)
                {
                    Logger.Warn($"Skipped unsafe chunk cache path: {chunk.Id}", "Install");
                    continue;
                }

                result.TryAdd(chunk.Id, new PredownloadChunkItem { Chunk = chunk, CachePath = cachePath });
            }
        }
        return result.Values.ToList();
    }

    private static bool IsChunkCacheReady(ChunkInfo chunk, string cachePath)
    {
        if (!File.Exists(cachePath) || new FileInfo(cachePath).Length != chunk.CompressedSize)
            return false;
        return string.IsNullOrWhiteSpace(chunk.CompressedMD5) || VerifyFileMD5(cachePath, chunk.CompressedMD5);
    }

    private async Task DownloadChunkCacheAsync(HttpClient httpClient, ChunkInfo chunk, string cachePath, CancellationToken ct)
    {
        if (IsChunkCacheReady(chunk, cachePath))
            return;

        var dir = Path.GetDirectoryName(cachePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmpPath = cachePath + "_tmp";

        const int maxRetries = 5;
        for (int retry = 0; ; retry++)
        {
            ct.ThrowIfCancellationRequested();
            long attemptBytes = 0;
            try
            {
                await using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    using var response = await httpClient.GetAsync(chunk.Url, HttpCompletionOption.ResponseHeadersRead, ct);
                    response.EnsureSuccessStatusCode();
                    await using var stream = await response.Content.ReadAsStreamAsync(ct);
                    var buffer = new byte[65536];
                    int read;
                    while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
                    {
                        await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                        ReportBytes(read);
                        attemptBytes += read;
                    }
                }

                if (new FileInfo(tmpPath).Length != chunk.CompressedSize)
                    throw new IOException($"Chunk size mismatch: {chunk.Id}");
                if (!string.IsNullOrWhiteSpace(chunk.CompressedMD5) && !VerifyFileMD5(tmpPath, chunk.CompressedMD5))
                    throw new IOException($"Chunk MD5 mismatch: {chunk.Id}");

                File.Move(tmpPath, cachePath, true);
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                if (attemptBytes > 0)
                    Interlocked.Add(ref _downloadedBytes, -attemptBytes);
                TryDeleteFile(tmpPath);
                if (retry >= maxRetries)
                    throw new IOException($"Download chunk failed: {chunk.Id}", ex);
                int delay = Math.Min(1000 * (1 << retry), 16000);
                Logger.Debug($"Retry chunk cache {retry + 1}: {chunk.Id} in {delay}ms ({ex.Message})", "Install");
                await Task.Delay(delay, ct);
            }
        }
    }

    // ================================================
    //  Package fallback
    // ================================================

    private async Task InstallViaPackageModeAsync(string gameBiz, string installPath, CancellationToken ct)
    {
        var packages = await HoYoPlayApiService.GetInstallPackagesAsync(gameBiz, ct);
        if (packages.Count == 0) { State = GameInstallState.Error; ErrorText = "无法获取安装包"; return; }

        string? hlPath = FindHardLinkSource(gameBiz, installPath);
        State = GameInstallState.Downloading;
        _totalBytes = packages.Sum(p => p.Size); _downloadedBytes = 0;
        StartSpeedTracking();

        foreach (var pkg in packages)
        {
            ct.ThrowIfCancellationRequested();
            var fn = Path.GetFileName(new Uri(pkg.Url).AbsolutePath.Split('?')[0]);
            var dp = Path.Combine(installPath, fn);
            if (hlPath != null)
            {
                var hl = Path.Combine(hlPath, fn);
                if (File.Exists(hl) && new FileInfo(hl).Length == pkg.Size && TryCreateHardLink(dp, hl))
                { _downloadedBytes += pkg.Size; ProgressPercent = (double)_downloadedBytes / _totalBytes * 100; continue; }
            }
            if (File.Exists(dp) && new FileInfo(dp).Length == pkg.Size)
            { _downloadedBytes += pkg.Size; ProgressPercent = (double)_downloadedBytes / _totalBytes * 100; continue; }

            await DownloadFileSimpleAsync(_httpClients[0], pkg.Url, dp, pkg.Size, pkg.MD5, ct);
            _downloadedBytes += pkg.Size;
            ProgressPercent = (double)_downloadedBytes / _totalBytes * 100;
        }

        State = GameInstallState.Extracting; StatusText = "正在解压...";
        var (ver, _) = await HoYoPlayApiService.GetLatestVersionsAsync(gameBiz, ct);
        var sdkVersion = await DownloadGameChannelSdkAsync(gameBiz, installPath, ct);
        SetGameConfigIni(gameBiz, installPath, ver, sdkVersion);
        CleanupTempFiles(installPath);
        State = GameInstallState.Finished; StatusText = "安装完成"; ProgressPercent = 100; StopSpeedTracking();
    }

    private async Task DownloadFileSimpleAsync(HttpClient hc, string url, string dest, long expected, string? md5, CancellationToken ct)
    {
        var tmp = dest + "_tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        using var resp = await hc.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        await using var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write);
        var buf = new byte[65536]; int r;
        while ((r = await stream.ReadAsync(buf, ct)) > 0) { await fs.WriteAsync(buf.AsMemory(0, r), ct); ReportBytes(r); }
        fs.Close();
        if (new FileInfo(tmp).Length != expected)
        {
            File.Delete(tmp);
            throw new IOException($"Size mismatch: {Path.GetFileName(dest)} expected={expected}");
        }
        if (!string.IsNullOrEmpty(md5) && !VerifyFileMD5(tmp, md5))
        {
            File.Delete(tmp);
            throw new IOException($"MD5 mismatch: {Path.GetFileName(dest)}");
        }
        File.Move(tmp, dest, true);
    }

    private async Task<string?> DownloadGameChannelSdkAsync(string gameBiz, string installPath, CancellationToken ct)
    {
        try
        {
            var channelSdk = await HoYoPlayApiService.GetGameChannelSDKAsync(gameBiz, ct);
            if (channelSdk?.ChannelSDKPackage?.Url == null)
                return null;

            if (string.Equals(ReadConfigValue(installPath, "sdk_version"), channelSdk.Version, StringComparison.OrdinalIgnoreCase))
                return channelSdk.Version;

            StatusText = "正在下载渠道 SDK...";
            var fileName = Path.GetFileName(new Uri(channelSdk.ChannelSDKPackage.Url).AbsolutePath);
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = "channel_sdk_pkg.zip";
            var packagePath = Path.Combine(installPath, fileName);

            await DownloadFileSimpleAsync(_httpClients[0], channelSdk.ChannelSDKPackage.Url, packagePath,
                channelSdk.ChannelSDKPackage.Size, channelSdk.ChannelSDKPackage.MD5, ct);

            StatusText = "正在解压渠道 SDK...";
            ExtractZipPackageSafe(packagePath, installPath);
            TryDeleteFile(packagePath);
            Logger.Info($"Channel SDK installed: {gameBiz} {channelSdk.Version}", "Install");
            return channelSdk.Version;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Logger.Warn($"DownloadGameChannelSdk failed: {ex.Message}", "Install");
            return null;
        }
    }

    private static string? ReadConfigValue(string installPath, string key)
    {
        try
        {
            var path = Path.Combine(installPath, "config.ini");
            if (!File.Exists(path)) return null;
            foreach (var line in File.ReadLines(path))
            {
                var i = line.IndexOf('=');
                if (i <= 0) continue;
                if (string.Equals(line[..i].Trim(), key, StringComparison.OrdinalIgnoreCase))
                    return line[(i + 1)..].Trim();
            }
        }
        catch { }
        return null;
    }

    private static void ExtractZipPackageSafe(string packagePath, string installPath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var root = Path.GetFullPath(installPath);
        if (!root.EndsWith(Path.DirectorySeparatorChar))
            root += Path.DirectorySeparatorChar;

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.FullName))
                continue;

            var fullPath = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"Unsafe path in channel SDK package: {entry.FullName}");

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(fullPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            var tempPath = fullPath + ".tmp";
            entry.ExtractToFile(tempPath, true);
            File.Move(tempPath, fullPath, true);
        }
    }

    // ================================================
    //  Hard link / Config / Cleanup
    // ================================================

    [System.Runtime.InteropServices.DllImport("Kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    private static bool TryCreateHardLink(string newPath, string existingPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(newPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var temp = newPath + ".link";
            if (File.Exists(temp)) File.Delete(temp);
            if (CreateHardLink(temp, existingPath, IntPtr.Zero)) { File.Move(temp, newPath, true); return true; }
            return false;
        }
        catch { return false; }
    }

    private static HashSet<string> GetInstalledAudioManifestFields(string? installPath)
    {
        var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(installPath) || !Directory.Exists(installPath))
            return fields;

        foreach (var pack in GetAudioPackStatus(installPath))
        {
            if (pack.IsInstalled && HoYoPlayApiService.IsAudioManifest(pack.Field))
                fields.Add(pack.Field);
        }

        return fields;
    }

    private string? FindHardLinkSource(string gameBiz, string installPath)
    {
        try
        {
            var game = new GameBiz(gameBiz).Game;
            var profile = GameProfiles.FindById(game);
            if (profile == null) return null;
            var root = Path.GetPathRoot(installPath);
            if (string.IsNullOrEmpty(root)) return null;
            if (!new DriveInfo(root).DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase)) return null;
            Version? best = null; string? bestPath = null;
            foreach (var s in new[] { "cn", "global", "bilibili" })
            {
                var other = $"{game}_{s}"; if (other == gameBiz) continue;
                foreach (var p in GetKnownGamePathCandidates(_session.Data, profile, s))
                {
                    if (!IsUsableGameDirectory(profile, p) ||
                        !string.Equals(Path.GetPathRoot(p), root, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Equals(Path.GetFullPath(installPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                        continue;
                    var detectedServer = GameSearchService.DetectServerFromConfig(p);
                    if (!string.IsNullOrEmpty(detectedServer) && !string.Equals(detectedServer, s, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var v = GameStateService.GetLocalVersion(p);
                    // A valid client without game_version is still a useful
                    // hard-link source. Prefer a known/newer version when one
                    // is available, matching Starward's candidate selection.
                    if (bestPath == null || v != null && (best == null || v > best))
                    {
                        best = v;
                        bestPath = p;
                    }
                }
            }
            if (bestPath != null) Logger.Info($"Hard link source: {bestPath}", "Install");
            else Logger.Info($"Hard link source not found for {gameBiz}", "Install");
            return bestPath;
        }
        catch { return null; }
    }

    private void SetGameConfigIni(string gameBiz, string installPath, string? version, string? sdkVersion = null)
    {
        try
        {
            var cp = Path.Combine(installPath, "config.ini");
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(cp))
                foreach (var line in File.ReadAllLines(cp))
                { if (line.StartsWith("[") || string.IsNullOrWhiteSpace(line)) continue; var i = line.IndexOf('='); if (i > 0) d[line[..i].Trim()] = line[(i + 1)..].Trim(); }
            d["game_version"] = version ?? "";
            d.Remove("predownload");
            var srv = new GameBiz(gameBiz).Server;
            if (srv == "cn") { d["channel"] = "1"; d["sub_channel"] = "1"; d["cps"] = "mihoyo"; }
            else if (srv == "global") { d["channel"] = "1"; d["sub_channel"] = "0"; d["cps"] = "hoyoverse"; }
            else if (srv == "bilibili") { d["channel"] = "14"; d["sub_channel"] = "0"; d["cps"] = "bilibili"; }
            d["sdk_version"] = sdkVersion ?? d.GetValueOrDefault("sdk_version", "");
            d["game_biz"] = HoYoPlayGameMap.ToHoYoPlayBiz(gameBiz);
            var sb = new StringBuilder(); sb.AppendLine("[General]");
            foreach (var kv in d) sb.AppendLine($"{kv.Key}={kv.Value}");
            File.WriteAllText(cp, sb.ToString());
        }
        catch (Exception ex) { Logger.Warn($"SetGameConfigIni: {ex.Message}", "Install"); }
    }

    private void MarkPredownload(string gameBiz, string ip, string? local, string? pre, IReadOnlyCollection<string>? audioManifestFields)
    {
        try
        {
            var p = Path.Combine(ip, "config.ini");
            if (!File.Exists(p)) return;

            var c = File.ReadAllText(p);
            c = Regex.Replace(c, @"(?m)^\s*predownload\s*=.*\r?\n?", "");
            var audio = audioManifestFields is { Count: > 0 } ? string.Join("|", audioManifestFields.OrderBy(x => x)) : "";
            c = c.TrimEnd() + $"\r\npredownload={local},{pre},{audio}\r\n";
            File.WriteAllText(p, c);
        }
        catch (Exception ex)
        {
            Logger.Warn($"MarkPredownload: {ex.Message}", "Install");
        }
    }

    private void CleanupTempFiles(string ip) { try { foreach (var f in Directory.GetFiles(ip, "*_tmp", SearchOption.AllDirectories)) TryDeleteFile(f); } catch { } }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch { }
    }

    private static string? ResolveSafeChildPath(string rootPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            return null;

        var root = Path.GetFullPath(rootPath);
        if (!root.EndsWith(Path.DirectorySeparatorChar))
            root += Path.DirectorySeparatorChar;

        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? fullPath : null;
    }

    // ================================================
    //  Audio Pack Download + Hard-link
    // ================================================

    /// <summary>
    /// Download a specific audio language pack and hard-link to all server versions.
    /// </summary>
    public async Task DownloadAudioPackAsync(string gameBiz, string installPath, string audioField, CancellationToken ct = default)
    {
        _cts?.Dispose();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _cts.Token;
        State = GameInstallState.Downloading;
        StatusText = $"正在获取 {audioField} 音频包清单...";
        ErrorText = "";

        try
        {
            var branch = await HoYoPlayApiService.GetGameBranchAsync(gameBiz, token);
            if (branch?.Main == null) { State = GameInstallState.Error; ErrorText = "无法获取版本信息"; return; }

            // Resolve plan but only keep files from the target audio manifest
            var plan = await HoYoPlayApiService.ResolveChunkDownloadPlanAsync(gameBiz, branch.Main, installPath, "",
                status => { StatusText = status; }, token, includeAudioManifests: true);
            if (plan == null) { State = GameInstallState.Error; ErrorText = "无法获取清单"; return; }

            // Keep only files from the target audio manifest
            var audioFiles = plan.Files.Where(f => f.ManifestField == audioField).ToList();
            if (audioFiles.Count == 0) { State = GameInstallState.Finished; StatusText = $"{audioField} 已是最新"; return; }

            var audioPlan = new ChunkDownloadPlan
            {
                GameBiz = gameBiz,
                InstallPath = installPath,
                Version = plan.Version,
                Files = audioFiles,
                TotalFiles = audioFiles.Count,
                TotalChunks = audioFiles.Sum(f => f.Chunks.Count),
            };
            int localMatched = await MarkLocalFilesAsync(audioPlan, installPath, token);
            if (localMatched > 0)
                Logger.Info($"Audio {audioField}: local matched {localMatched}/{audioFiles.Count}", "Install");

            // Hard-link from other server versions
            var game = new GameBiz(gameBiz).Game;
            var profile = GameProfiles.FindById(game);
            if (profile == null) { State = GameInstallState.Error; ErrorText = "未知游戏"; return; }
            var root = Path.GetPathRoot(installPath);
            var normalizedInstallPath = Path.GetFullPath(installPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var siblingInstallPaths = new[] { "cn", "global", "bilibili" }
                .SelectMany(server => GetKnownGamePathCandidates(_session.Data, profile, server))
                .Where(path => string.Equals(Path.GetPathRoot(path), root, StringComparison.OrdinalIgnoreCase))
                .Where(path => !string.Equals(path, normalizedInstallPath, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            int hardLinked = 0;
            foreach (var otherPath in siblingInstallPaths)
            {
                foreach (var file in audioFiles.Where(f => !f.IsFinished))
                {
                    if (TryHardLinkBySize(file, otherPath))
                        hardLinked++;
                }
            }
            Logger.Info($"Audio {audioField}: hard-linked {hardLinked}/{audioFiles.Count} from other servers", "Install");

            var toDownload = audioFiles.Where(f => !f.IsFinished).ToList();
            if (toDownload.Count == 0)
            {
                State = GameInstallState.Finished; StatusText = $"{audioField} 音频包安装完成 (全部硬链接)";
                ProgressPercent = 100; return;
            }

            long remainingBytes = toDownload.Sum(f => f.Chunks.Sum(c => c.CompressedSize));
            _totalBytes = remainingBytes; _downloadedBytes = 0;
            StatusText = $"正在下载 {audioField} ({FormatBytes(remainingBytes)})...";
            StartSpeedTracking();

            int dlDone = 0;
            await RunParallelWithCancelAsync(toDownload, token, async (file, ct2) =>
            {
                var httpClient = _httpClients[Environment.CurrentManagedThreadId % _httpClients.Length];
                await DownloadChunksToFileAsync(httpClient, file, ct2);
                file.IsFinished = true;
                Interlocked.Increment(ref dlDone);
            });

            if (token.IsCancellationRequested) { State = GameInstallState.Paused; StatusText = "已暂停"; StopSpeedTracking(); return; }

            StopSpeedTracking();
            State = GameInstallState.Finished; StatusText = $"{audioField} 音频包安装完成"; ProgressPercent = 100;
        }
        catch (OperationCanceledException) { State = GameInstallState.Paused; StatusText = "已暂停"; StopSpeedTracking(); }
        catch (Exception ex) { Logger.Error($"Audio download failed: {ex}", "Install"); State = GameInstallState.Error; ErrorText = ex.Message; StopSpeedTracking(); }
    }

    /// <summary>
    /// Check which audio packs are installed in the game directory.
    /// </summary>
    public static List<AudioPackInfo> GetAudioPackStatus(string installPath, GameProfile? profile = null, GameBiz? biz = null)
    {
        var result = new List<AudioPackInfo>();
        var knownPacks = new[] {
            new AudioPackDefinition("zh-cn", "Chinese (zh-cn)", "Chinese", "Audio_Chinese_pkg_version", new[] { "Chinese(PRC)", "Chinese", "zh-cn" }),
            new AudioPackDefinition("en-us", "English (en-us)", "English(US)", "Audio_English(US)_pkg_version", new[] { "English(US)", "English", "en-us" }),
            new AudioPackDefinition("ja-jp", "Japanese (ja-jp)", "Japanese", "Audio_Japanese_pkg_version", new[] { "Japanese", "ja-jp" }),
            new AudioPackDefinition("ko-kr", "Korean (ko-kr)", "Korean", "Audio_Korean_pkg_version", new[] { "Korean", "ko-kr" }),
        };

        var dataDirs = GetAudioScanDataDirs(installPath, profile, biz).ToList();
        foreach (var pack in knownPacks)
        {
            bool installed = IsAudioPackInstalled(installPath, dataDirs, pack);
            result.Add(new AudioPackInfo { Field = pack.Field, DisplayName = pack.DisplayName, IsInstalled = installed });
        }
        return result;
    }

    private static IEnumerable<string> GetAudioScanDataDirs(string installPath, GameProfile? profile, GameBiz? biz)
    {
        var names = new List<string>();
        if (profile != null && biz != null)
            names.Add(profile.GetDataFolder(biz.Value));
        names.AddRange(new[] { "YuanShen_Data", "GenshinImpact_Data", "StarRail_Data", "ZenlessZoneZero_Data", "BH3_Data" });

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            var path = Path.Combine(installPath, name);
            if (Directory.Exists(path) && seen.Add(path))
                yield return path;
        }
    }

    private sealed record AudioPackDefinition(
        string Field,
        string DisplayName,
        string ScanMarker,
        string PackageVersionFileName,
        string[] FileMarkers);

    private static bool IsAudioPackInstalled(string installPath, List<string> dataDirs, AudioPackDefinition pack)
    {
        if (HasExpectedPackageVersionFile(installPath, pack.PackageVersionFileName))
            return true;

        foreach (var dataDir in dataDirs)
        {
            if (HasExpectedPackageVersionFile(dataDir, pack.PackageVersionFileName))
                return true;

            var streamingAssets = Path.Combine(dataDir, "StreamingAssets");
            var audioRoots = new[]
            {
                Path.Combine(streamingAssets, "AudioAssets"),
                Path.Combine(streamingAssets, "Audio"),
                Path.Combine(streamingAssets, "Audio", "Windows"),
                Path.Combine(streamingAssets, "Audio", "GeneratedSoundBanks", "Windows"),
                Path.Combine(streamingAssets, "Audio", "AudioPackage", "Windows", "DecodedBanks"),
            };

            foreach (var root in audioRoots.Where(Directory.Exists))
            {
                if (HasMarkedDirectoryWithFiles(root, pack.FileMarkers) || HasMarkedAudioFile(root, pack.FileMarkers))
                    return true;
            }
        }

        return HasAudioLanguageStateFile(installPath, pack.ScanMarker);
    }

    private static bool HasMarkedDirectoryWithFiles(string root, string[] markers)
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).Take(4096))
            {
                if (!ContainsAny(Path.GetFileName(dir), markers)) continue;
                if (Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Any())
                    return true;
            }
        }
        catch { }
        return false;
    }

    private static bool HasMarkedAudioFile(string root, string[] markers)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Take(12000))
            {
                var fileName = Path.GetFileName(file);
                if (fileName.Contains("pkg_version", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (ContainsAny(fileName, markers))
                    return true;
            }
        }
        catch { }
        return false;
    }

    private static bool HasExpectedPackageVersionFile(string root, string packageVersionFileName)
    {
        try
        {
            if (!Directory.Exists(root))
                return false;

            foreach (var file in Directory.EnumerateFiles(root, packageVersionFileName, SearchOption.AllDirectories).Take(8))
            {
                if (Path.GetFileName(file).Equals(packageVersionFileName, StringComparison.OrdinalIgnoreCase)
                    && new FileInfo(file).Length > 0)
                {
                    return true;
                }
            }
        }
        catch { }
        return false;
    }

    private static bool HasAudioLanguageStateFile(string installPath, string scanMarker)
    {
        try
        {
            if (!Directory.Exists(installPath))
                return false;

            foreach (var file in Directory.EnumerateFiles(installPath, "*Audio*Language*", SearchOption.AllDirectories).Take(16))
            {
                if (File.ReadLines(file).Any(line => line.Contains(scanMarker, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
        }
        catch { }
        return false;
    }

    private static bool ContainsAny(string value, string[] markers)
    {
        foreach (var marker in markers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string FormatSpeed(long bps) { const double KB = 1024, MB = 1024 * 1024; return bps >= MB ? $"{bps / MB:F1} MB/s" : bps >= KB ? $"{bps / KB:F1} KB/s" : $"{bps} B/s"; }
    private static string FormatBytes(long b) { const double KB = 1024, MB = 1024 * 1024, GB = 1024.0 * 1024 * 1024; return b >= GB ? $"{b / GB:F2} GB" : b >= MB ? $"{b / MB:F1} MB" : b >= KB ? $"{b / KB:F1} KB" : $"{b} B"; }
    private static string FormatRemainingTime(long remainingBytes, double bytesPerSecond)
    {
        if (remainingBytes <= 0) return "00:00:00";
        if (bytesPerSecond <= 0) return "--:--:--";
        var seconds = Math.Min(remainingBytes / bytesPerSecond, TimeSpan.MaxValue.TotalSeconds);
        var remaining = TimeSpan.FromSeconds(seconds);
        return remaining.TotalDays >= 1
            ? $"{(int)remaining.TotalDays}.{remaining:hh\\:mm\\:ss}"
            : remaining.ToString(@"hh\:mm\:ss");
    }
}

public class AudioPackInfo
{
    public string Field { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool IsInstalled { get; set; }
    public string ButtonText => IsInstalled ? "已安装" : "安装";
    public bool CanInstall => !IsInstalled;
}

public class GameResourceIssue
{
    public string RelativePath { get; set; } = "";
    public long Size { get; set; }
    public string MD5 { get; set; } = "";
    public string ManifestField { get; set; } = "";
    public string Reason { get; set; } = "";
}

public class GameResourceVerificationResult
{
    public int TotalFiles { get; set; }
    public int ValidFiles { get; set; }
    public int MissingFiles { get; set; }
    public int InvalidFiles { get; set; }
    public List<GameResourceIssue> Issues { get; set; } = new();
    public bool IsHealthy => MissingFiles == 0 && InvalidFiles == 0;
}
