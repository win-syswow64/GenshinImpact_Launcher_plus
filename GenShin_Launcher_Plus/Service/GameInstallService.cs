﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GenShin_Launcher_Plus.Helper;
using GenShin_Launcher_Plus.Models;
using GenShin_Launcher_Plus.Models.HoYoPlay;
using ZstdSharp;

namespace GenShin_Launcher_Plus.Service;

public class GameInstallService : INotifyPropertyChanged
{
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

    // === Bindable ===
    private GameInstallState _state = GameInstallState.Idle;
    public GameInstallState State { get => _state; set { _state = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsInstalling)); OnPropertyChanged(nameof(StateText)); } }

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

    private long _totalBytes;
    public long TotalBytes { get => _totalBytes; set { _totalBytes = value; OnPropertyChanged(); } }

    private long _downloadedBytes;
    public long DownloadedBytes { get => _downloadedBytes; set { _downloadedBytes = value; OnPropertyChanged(); } }

    private string _errorText = "";
    public string ErrorText { get => _errorText; set { _errorText = value; OnPropertyChanged(); } }

    public bool IsInstalling => State is GameInstallState.Downloading or GameInstallState.Preparing or GameInstallState.Extracting or GameInstallState.Verifying;

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
    private Timer? _speedTimer;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void StartSpeedTracking()
    {
        _speedBytesAccumulator = 0; _lastSpeedTickMs = Environment.TickCount64;
        _speedTimer?.Dispose();
        _speedTimer = new Timer(_ =>
        {
            try
            {
                var now = Environment.TickCount64; var elapsed = now - _lastSpeedTickMs;
                if (elapsed > 0)
                {
                    var speed = Interlocked.Exchange(ref _speedBytesAccumulator, 0) * 1000 / elapsed;
                    DownloadSpeedText = FormatSpeed(speed);
                    BytesProgressText = $"{FormatBytes(Volatile.Read(ref _downloadedBytes))} / {FormatBytes(Volatile.Read(ref _totalBytes))}";
                    _lastSpeedTickMs = now;
                }
            }
            catch { }
        }, null, 1000, 1000);
    }

    private void StopSpeedTracking() { _speedTimer?.Dispose(); _speedTimer = null; DownloadSpeedText = ""; BytesProgressText = ""; }
    private void ReportBytes(long n) { Interlocked.Add(ref _downloadedBytes, n); Interlocked.Add(ref _speedBytesAccumulator, n); }

    // === Helpers ===

    public static List<string> GetExistingGameDrives()
    {
        var drives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in GameProfiles.All)
            foreach (var server in new[] { "cn", "global", "bilibili" })
            {
                var p = App.Current.DataModel.GetGamePath($"{profile.Id}_{server}");
                if (!string.IsNullOrEmpty(p) && Directory.Exists(p))
                {
                    var r = Path.GetPathRoot(p);
                    if (!string.IsNullOrEmpty(r)) drives.Add(r);
                }
            }
        return drives.ToList();
    }

    public static string GetDefaultInstallDir(string gameBiz)
    {
        var game = new GameBiz(gameBiz).Game;
        var folder = game switch { "genshin" => "Genshin Impact", "starrail" => "Star Rail", "zzz" => "ZenlessZoneZero", "honkai3" => "Honkai Impact 3rd", _ => "miHoYo Game" };
        var drives = GetExistingGameDrives();
        if (drives.Count > 0) return Path.Combine(drives[0], "Program Files", "miHoYo Launcher", "games", folder);
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), folder);
    }

    // ================================================
    //  Install
    // ================================================

    public async Task InstallGameAsync(string gameBiz, string installPath, CancellationToken ct = default)
    {
        _cts?.Dispose();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _cts.Token;
        State = GameInstallState.Preparing;
        StatusText = "正在获取游戏版本信息...";
        ProgressPercent = 0;
        ErrorText = "";

        try
        {
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
                var toLink = plan.Files.Where(f => !f.IsFinished).ToList();
                int hlTotal = toLink.Count, hlDone = 0;

            // Run hard link creation on background thread (MD5 verification is CPU-bound)
                var hlResult = await Task.Run(() =>
                {
                    int linked = 0;
                    foreach (var file in toLink)
                    {
                        token.ThrowIfCancellationRequested();
                        if (TryHardLinkBySize(file, hardLinkPath))
                            linked++;
                        var done = Interlocked.Increment(ref hlDone);

                        // Report progress every 50 files
                        if (done % 50 == 0 || done == hlTotal)
                        {
                            ProgressPercent = (double)done / hlTotal * 100;
                            StatusText = $"正在创建硬链接 ({done}/{hlTotal}, 成功 {linked})...";
                        }
                    }
                    return linked;
                }, token);
                hardLinked = hlResult;
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
                SetGameConfigIni(gameBiz, installPath, plan.Version);
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
                var done = Interlocked.Increment(ref dlDone);
                ProgressPercent = (double)done / toDownload.Count * 100;
            });

            if (token.IsCancellationRequested) { State = GameInstallState.Paused; StatusText = "已暂停"; StopSpeedTracking(); return; }

            // ── Phase 5: Config ──
            StopSpeedTracking();
            State = GameInstallState.Verifying; StatusText = "正在写入配置...";
            SetGameConfigIni(gameBiz, installPath, plan.Version);
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
                SetGameConfigIni(gameBiz, installPath, plan.Version);
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
                var done = Interlocked.Increment(ref dlDone);
                ProgressPercent = (double)done / toDownload.Count * 100;
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
            var plan = await HoYoPlayApiService.ResolveChunkDownloadPlanAsync(gameBiz, branch.PreDownload, installPath, localVersion?.ToString() ?? "",
                status => { StatusText = status; Logger.Debug(status, "PreDownload"); }, token);
            if (plan == null || plan.Files.Count == 0) { State = GameInstallState.Finished; StatusText = "无预下载"; return; }

            var chunks = BuildPredownloadChunkList(plan, installPath);
            var toDownload = chunks.Where(x => !IsChunkCacheReady(x.Chunk, x.CachePath)).ToList();
            long remainingBytes = toDownload.Sum(x => x.Chunk.CompressedSize);

            if (toDownload.Count == 0)
            {
                MarkPredownload(gameBiz, installPath, localVersion?.ToString(), branch.PreDownload.Tag);
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
                var done = Interlocked.Increment(ref dlDone);
                ProgressPercent = (double)done / toDownload.Count * 100;
            });

            if (token.IsCancellationRequested) { State = GameInstallState.Paused; StatusText = "已暂停"; StopSpeedTracking(); return; }

            MarkPredownload(gameBiz, installPath, localVersion?.ToString(), branch.PreDownload.Tag);
            StopSpeedTracking();
            State = GameInstallState.Finished; StatusText = "预下载完成"; ProgressPercent = 100;
        }
        catch (OperationCanceledException) { State = GameInstallState.Paused; StatusText = "已暂停"; StopSpeedTracking(); }
        catch (Exception ex) { Logger.Error($"Predownload failed: {ex}", "Install"); State = GameInstallState.Error; ErrorText = ex.Message; StopSpeedTracking(); }
    }

    public void Pause() { try { _cts?.Cancel(); } catch { } }

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
    /// Try to hard-link a file from another game installation by matching path + size.
    /// Size check is sufficient - for the same game version, same relative path + same size
    /// means the file content is identical. No need for expensive MD5 computation.
    /// </summary>
    private bool TryHardLinkBySize(ChunkDownloadFile file, string hardLinkSourcePath)
    {
        if (file.IsFinished) return false;

        // Try exact same relative path
        var target = ResolveSafeChildPath(hardLinkSourcePath, file.RelativePath);
        if (target == null) return false;
        if (TryHardLinkMatchSize(file, target))
            return true;

        // For Genshin: try YuanShen_Data <-> GenshinImpact_Data swap
        var rel = file.RelativePath;
        if (rel.Contains("YuanShen_Data"))
        {
            target = ResolveSafeChildPath(hardLinkSourcePath, rel.Replace("YuanShen_Data", "GenshinImpact_Data"));
            if (target == null) return false;
            if (TryHardLinkMatchSize(file, target)) return true;
        }
        else if (rel.Contains("GenshinImpact_Data"))
        {
            target = ResolveSafeChildPath(hardLinkSourcePath, rel.Replace("GenshinImpact_Data", "YuanShen_Data"));
            if (target == null) return false;
            if (TryHardLinkMatchSize(file, target)) return true;
        }

        return false;
    }

    private bool TryHardLinkMatchSize(ChunkDownloadFile file, string sourcePath)
    {
        if (!File.Exists(sourcePath)) return false;
        var sourceLen = new FileInfo(sourcePath).Length;
        if (sourceLen != file.Size) return false;
        try
        {
            // Verify MD5 if available (following Starward pattern)
            if (!string.IsNullOrEmpty(file.MD5))
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
        SetGameConfigIni(gameBiz, installPath, ver);
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
                var p = App.Current.DataModel.GetExactGamePath(other);
                if (string.IsNullOrEmpty(p) || !Directory.Exists(p))
                    p = GameSearchService.FindGame(profile, s)?.Path;
                if (string.IsNullOrEmpty(p) || !Directory.Exists(p) || Path.GetPathRoot(p) != root) continue;
                if (Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Equals(Path.GetFullPath(installPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) continue;
                var detectedServer = GameSearchService.DetectServerFromConfig(p);
                if (!string.IsNullOrEmpty(detectedServer) && !string.Equals(detectedServer, s, StringComparison.OrdinalIgnoreCase)) continue;
                var exe = profile.GetExeName(new GameBiz(other));
                if (!File.Exists(Path.Combine(p, exe))) continue;
                var v = GameStateService.GetLocalVersion(p);
                if (v != null && (best == null || v > best)) { best = v; bestPath = p; }
            }
            if (bestPath != null) Logger.Info($"Hard link source: {bestPath}", "Install");
            else Logger.Info($"Hard link source not found for {gameBiz}", "Install");
            return bestPath;
        }
        catch { return null; }
    }

    private void SetGameConfigIni(string gameBiz, string installPath, string? version)
    {
        try
        {
            var cp = Path.Combine(installPath, "config.ini");
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(cp))
                foreach (var line in File.ReadAllLines(cp))
                { if (line.StartsWith("[") || string.IsNullOrWhiteSpace(line)) continue; var i = line.IndexOf('='); if (i > 0) d[line[..i].Trim()] = line[(i + 1)..].Trim(); }
            d["game_version"] = version ?? "";
            var srv = new GameBiz(gameBiz).Server;
            if (srv == "cn") { d["channel"] = "1"; d["sub_channel"] = "1"; d["cps"] = "mihoyo"; }
            else if (srv == "global") { d["channel"] = "1"; d["sub_channel"] = "0"; d["cps"] = "hoyoverse"; }
            else if (srv == "bilibili") { d["channel"] = "14"; d["sub_channel"] = "0"; d["cps"] = "bilibili"; }
            d["game_biz"] = HoYoPlayGameMap.ToHoYoPlayBiz(gameBiz);
            var sb = new StringBuilder(); sb.AppendLine("[General]");
            foreach (var kv in d) sb.AppendLine($"{kv.Key}={kv.Value}");
            File.WriteAllText(cp, sb.ToString());
        }
        catch (Exception ex) { Logger.Warn($"SetGameConfigIni: {ex.Message}", "Install"); }
    }

    private void MarkPredownload(string gameBiz, string ip, string? local, string? pre)
    {
        try { var p = Path.Combine(ip, "config.ini"); if (File.Exists(p)) { var c = File.ReadAllText(p); c = Regex.Replace(c, @"predownload=.*", ""); c += $"\npredownload={local},{pre},\n"; File.WriteAllText(p, c); } } catch { }
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
            var root = Path.GetPathRoot(installPath);
            int hardLinked = 0;
            foreach (var server in new[] { "cn", "global", "bilibili" })
            {
                var otherBiz = $"{game}_{server}";
                if (otherBiz == gameBiz) continue;
                var otherPath = App.Current.DataModel.GetExactGamePath(otherBiz);
                if (string.IsNullOrEmpty(otherPath) || !Directory.Exists(otherPath)) continue;
                if (Path.GetPathRoot(otherPath) != root) continue;

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
                var done = Interlocked.Increment(ref dlDone);
                ProgressPercent = (double)done / toDownload.Count * 100;
            });

            if (token.IsCancellationRequested) { State = GameInstallState.Paused; StatusText = "已暂停"; StopSpeedTracking(); return; }

            // Hard-link downloaded files to other servers
            foreach (var server in new[] { "cn", "global", "bilibili" })
            {
                var otherBiz = $"{game}_{server}";
                if (otherBiz == gameBiz) continue;
                var otherPath = App.Current.DataModel.GetExactGamePath(otherBiz);
                if (string.IsNullOrEmpty(otherPath) || !Directory.Exists(otherPath)) continue;
                if (Path.GetPathRoot(otherPath) != root) continue;
                foreach (var file in toDownload)
                {
                    var target = ResolveSafeChildPath(otherPath, file.RelativePath);
                    if (target == null) continue;
                    TryCreateHardLink(target, file.FullPath);
                }
                Logger.Info($"Hard-linked {audioField} to {otherBiz}", "Install");
            }

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
            ("zh-cn", "Chinese (zh-cn)", new[] { "Chinese(PRC)", "Chinese", "zh-cn" }),
            ("en-us", "English (en-us)", new[] { "English(US)", "English", "en-us" }),
            ("ja-jp", "Japanese (ja-jp)", new[] { "Japanese", "ja-jp" }),
            ("ko-kr", "Korean (ko-kr)", new[] { "Korean", "ko-kr" }),
        };

        var dataDirs = GetAudioScanDataDirs(installPath, profile, biz).ToList();
        foreach (var (field, displayName, markers) in knownPacks)
        {
            bool installed = IsAudioPackInstalled(installPath, dataDirs, markers);
            result.Add(new AudioPackInfo { Field = field, DisplayName = displayName, IsInstalled = installed });
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

    private static bool IsAudioPackInstalled(string installPath, List<string> dataDirs, string[] markers)
    {
        foreach (var dataDir in dataDirs)
        {
            var streamingAssets = Path.Combine(dataDir, "StreamingAssets");
            var audioRoots = new[]
            {
                Path.Combine(streamingAssets, "AudioAssets"),
                Path.Combine(streamingAssets, "Audio"),
                Path.Combine(streamingAssets, "Audio", "Windows"),
                Path.Combine(streamingAssets, "Audio", "GeneratedSoundBanks", "Windows"),
            };

            foreach (var root in audioRoots.Where(Directory.Exists))
            {
                if (HasMarkedDirectoryWithFiles(root, markers) || HasMarkedAudioFile(root, markers))
                    return true;
            }

            if (HasMarkedPackageVersion(dataDir, markers))
                return true;
        }

        return HasMarkedPackageVersion(installPath, markers);
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
                if (ContainsAny(file, markers))
                    return true;
            }
        }
        catch { }
        return false;
    }

    private static bool HasMarkedPackageVersion(string root, string[] markers)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*pkg_version*", SearchOption.AllDirectories).Take(256))
            {
                if (ContainsAny(Path.GetFileName(file), markers))
                    return true;
                try
                {
                    if (ContainsAny(File.ReadAllText(file), markers))
                        return true;
                }
                catch { }
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
}

public class AudioPackInfo
{
    public string Field { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool IsInstalled { get; set; }
    public string ButtonText => IsInstalled ? "已安装" : "安装";
    public bool CanInstall => !IsInstalled;
}
