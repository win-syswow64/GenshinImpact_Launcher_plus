using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenShin_Launcher_Plus.Helper;
using GenShin_Launcher_Plus.Models;
using GenShin_Launcher_Plus.Models.HoYoPlay;
using GenShin_Launcher_Plus.Models.Sophon;
using ZstdSharp;

namespace GenShin_Launcher_Plus.Service;

/// <summary>
/// HoYoPlay API client with full Sophon chunk support.
/// </summary>
public static class HoYoPlayApiService
{
    private static readonly HttpClient _httpClient = new(new HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All,
    });

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    #region API Calls

    public static async Task<GamePackageInfo?> GetGamePackageAsync(string gameBiz, CancellationToken ct = default)
    {
        try
        {
            var launcherId = HoYoPlayGameMap.GetLauncherId(gameBiz);
            var gameId = HoYoPlayGameMap.GetApiGameId(gameBiz);
            var baseUrl = HoYoPlayGameMap.GetApiBaseUrl(gameBiz);
            if (launcherId == null || gameId == null) return null;

            var url = $"{baseUrl}getGamePackages?launcher_id={launcherId}&language=zh-cn&game_ids[]={gameId}";
            Logger.Debug($"Fetching game package: {url}", "HoYoPlay");
            var json = await _httpClient.GetStringAsync(url, ct).ConfigureAwait(false);
            var resp = JsonSerializer.Deserialize<HoYoApiResponse<GamePackageResponse>>(json, _jsonOptions);
            if (resp?.Retcode != 0) { Logger.Warn($"GetGamePackage error: {resp?.Retcode} {resp?.Message}", "HoYoPlay"); return null; }
            return resp?.Data?.GamePackages?.FirstOrDefault(x => x.Game?.Id == gameId);
        }
        catch (Exception ex) { Logger.Warn($"GetGamePackage: {ex.Message}", "HoYoPlay"); return null; }
    }

    public static async Task<GameBranchInfo?> GetGameBranchAsync(string gameBiz, CancellationToken ct = default)
    {
        try
        {
            var launcherId = HoYoPlayGameMap.GetLauncherId(gameBiz);
            var gameId = HoYoPlayGameMap.GetApiGameId(gameBiz);
            var baseUrl = HoYoPlayGameMap.GetApiBaseUrl(gameBiz);
            if (launcherId == null || gameId == null) return null;

            var url = $"{baseUrl}getGameBranches?launcher_id={launcherId}&language=zh-cn&game_ids[]={gameId}";
            Logger.Debug($"Fetching game branch: {url}", "HoYoPlay");
            var json = await _httpClient.GetStringAsync(url, ct).ConfigureAwait(false);
            var resp = JsonSerializer.Deserialize<HoYoApiResponse<GameBranchResponse>>(json, _jsonOptions);
            if (resp?.Retcode != 0) { Logger.Warn($"GetGameBranch error: {resp?.Retcode} {resp?.Message}", "HoYoPlay"); return null; }
            return resp?.Data?.GameBranches?.FirstOrDefault(x => x.Game?.Id == gameId);
        }
        catch (Exception ex) { Logger.Warn($"GetGameBranch: {ex.Message}", "HoYoPlay"); return null; }
    }

    public static async Task<(string? Latest, string? Predownload)> GetLatestVersionsAsync(string gameBiz, CancellationToken ct = default)
    {
        var branch = await GetGameBranchAsync(gameBiz, ct).ConfigureAwait(false);
        if (branch?.Main?.Tag != null) return (branch.Main.Tag, branch.PreDownload?.Tag);
        var package = await GetGamePackageAsync(gameBiz, ct).ConfigureAwait(false);
        if (package?.Main?.Major?.Version != null) return (package.Main.Major.Version, package.PreDownload?.Major?.Version);
        return (null, null);
    }

    public static async Task<GameChannelSdkInfo?> GetGameChannelSDKAsync(string gameBiz, CancellationToken ct = default)
    {
        try
        {
            var launcherId = HoYoPlayGameMap.GetLauncherId(gameBiz);
            var gameId = HoYoPlayGameMap.GetApiGameId(gameBiz);
            var baseUrl = HoYoPlayGameMap.GetApiBaseUrl(gameBiz);
            if (launcherId == null || gameId == null) return null;

            var (channel, subChannel) = HoYoPlayGameMap.GetChannelInfo(gameBiz);
            var url = $"{baseUrl}getGameChannelSDKs?launcher_id={launcherId}&language=zh-cn&game_ids[]={gameId}&channel={channel}&sub_channel={subChannel}";
            Logger.Debug($"Fetching game channel SDK: {url}", "HoYoPlay");
            var json = await _httpClient.GetStringAsync(url, ct).ConfigureAwait(false);
            var resp = JsonSerializer.Deserialize<HoYoApiResponse<GameChannelSdkResponse>>(json, _jsonOptions);
            if (resp?.Retcode != 0) { Logger.Warn($"GetGameChannelSDK error: {resp?.Retcode} {resp?.Message}", "HoYoPlay"); return null; }
            return resp?.Data?.GameChannelSDKs?.FirstOrDefault(x => x.Game?.Id == gameId);
        }
        catch (Exception ex) { Logger.Warn($"GetGameChannelSDK: {ex.Message}", "HoYoPlay"); return null; }
    }

    /// <summary>
    /// Get Sophon chunk build from downloader API
    /// </summary>
    public static async Task<SophonChunkBuildResponse?> GetSophonChunkBuildAsync(
        string gameBiz, GameBranchPackageInfo branchPackage, string tag = "", CancellationToken ct = default)
    {
        try
        {
            var sophonBase = HoYoPlayGameMap.GetSophonBaseUrl(gameBiz);
            var url = $"{sophonBase}getBuild?branch={branchPackage.Branch}&package_id={branchPackage.PackageId}&password={branchPackage.Password}";
            if (!string.IsNullOrEmpty(tag)) url += $"&tag={tag}";

            Logger.Debug($"Fetching Sophon chunk build: {url}", "HoYoPlay");
            var json = await _httpClient.GetStringAsync(url, ct);
            var resp = JsonSerializer.Deserialize<HoYoApiResponse<SophonChunkBuildResponse>>(json, _jsonOptions);
            if (resp?.Retcode != 0) { Logger.Warn($"GetSophonChunkBuild error: {resp?.Retcode}", "HoYoPlay"); return null; }
            return resp?.Data;
        }
        catch (Exception ex) { Logger.Warn($"GetSophonChunkBuild: {ex.Message}", "HoYoPlay"); return null; }
    }

    /// <summary>
    /// Download and parse a Sophon manifest (protobuf, zstd-compressed)
    /// </summary>
    public static async Task<SophonChunkManifest?> DownloadChunkManifestAsync(
        SophonDownloadUrl manifestDownload, SophonManifestFileInfo manifestFile, CancellationToken ct = default)
    {
        try
        {
            var cacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "Cache", "manifest");
            Directory.CreateDirectory(cacheDir);
            var cacheFileName = Path.GetFileName(manifestFile.Id);
            if (string.IsNullOrWhiteSpace(cacheFileName)) return null;
            var cacheFile = Path.Combine(cacheDir, cacheFileName);

            byte[] raw;

            // Check cache
            if (File.Exists(cacheFile) && new FileInfo(cacheFile).Length == manifestFile.CompressedSize)
            {
                raw = await File.ReadAllBytesAsync(cacheFile, ct);
                using var md5 = MD5.Create();
                using var ds = new DecompressionStream(new MemoryStream(raw));
                using var ms = new MemoryStream();
                await ds.CopyToAsync(ms, ct);
                ms.Position = 0;
                var hash = Convert.ToHexString(await md5.ComputeHashAsync(ms, ct));
                if (string.Equals(hash, manifestFile.Checksum, StringComparison.OrdinalIgnoreCase))
                {
                    ms.Position = 0;
                    var manifest = SophonChunkManifest.Parser.ParseFrom(ms);
                    Logger.Debug($"Manifest loaded from cache: {manifestFile.Id} ({manifest.Chuncks.Count} files)", "Sophon");
                    return manifest;
                }
            }

            // Download
            var url = $"{manifestDownload.UrlPrefix.TrimEnd('/')}/{Uri.EscapeDataString(manifestFile.Id)}";
            Logger.Debug($"Downloading manifest: {url}", "Sophon");
            var data = await _httpClient.GetByteArrayAsync(url, ct);
            await File.WriteAllBytesAsync(cacheFile, data, ct);

            // Decompress zstd and parse protobuf
            using var decompressed = new DecompressionStream(new MemoryStream(data));
            using var result = new MemoryStream();
            await decompressed.CopyToAsync(result, ct);
            result.Position = 0;

            var parsed = SophonChunkManifest.Parser.ParseFrom(result);
            Logger.Debug($"Manifest parsed: {manifestFile.Id} ({parsed.Chuncks.Count} files)", "Sophon");
            return parsed;
        }
        catch (Exception ex)
        {
            Logger.Warn($"DownloadChunkManifest failed: {ex.Message}", "Sophon");
            return null;
        }
    }

    #endregion

    public static async Task<List<GamePackageFileInfo>> GetInstallPackagesAsync(string gameBiz, CancellationToken ct = default)
    {
        var package = await GetGamePackageAsync(gameBiz, ct);
        var result = new List<GamePackageFileInfo>();
        if (package?.Main?.Major?.GamePackages != null) result.AddRange(package.Main.Major.GamePackages);
        if (package?.Main?.Major?.AudioPackages != null) result.AddRange(package.Main.Major.AudioPackages);
        return result;
    }

    public static async Task<List<GamePackageFileInfo>> GetPredownloadPackagesAsync(string gameBiz, CancellationToken ct = default)
    {
        var package = await GetGamePackageAsync(gameBiz, ct);
        var result = new List<GamePackageFileInfo>();
        if (package?.PreDownload?.Major?.GamePackages != null) result.AddRange(package.PreDownload.Major.GamePackages);
        if (package?.PreDownload?.Major?.AudioPackages != null) result.AddRange(package.PreDownload.Major.AudioPackages);
        return result;
    }

    #region High-level methods

    /// <summary>
    /// Resolve the download plan for a game install/update/predownload.
    /// Returns a list of chunk download tasks with URLs, sizes, and target file paths.
    /// </summary>
    public static async Task<ChunkDownloadPlan?> ResolveChunkDownloadPlanAsync(
        string gameBiz, GameBranchPackageInfo branchPackage, string installPath,
        string localVersionTag = "", Action<string>? statusCallback = null,
        CancellationToken ct = default, bool includeAudioManifests = false,
        IReadOnlySet<string>? audioManifestFields = null)
    {
        var plan = new ChunkDownloadPlan { GameBiz = gameBiz, InstallPath = installPath };

        var sophonBuild = await GetSophonChunkBuildAsync(gameBiz, branchPackage, "", ct);
        if (sophonBuild == null || sophonBuild.Manifests == null)
        {
            Logger.Warn("Sophon build is null, falling back to package mode", "Sophon");
            return null;
        }

        plan.Version = sophonBuild.Tag ?? branchPackage.Tag;

        SophonChunkBuildResponse? localBuild = null;
        if (!string.IsNullOrWhiteSpace(localVersionTag) &&
            !string.Equals(localVersionTag, plan.Version, StringComparison.OrdinalIgnoreCase))
        {
            localBuild = await GetSophonChunkBuildAsync(gameBiz, branchPackage, localVersionTag, ct);
            Logger.Info(localBuild?.Manifests is null
                ? $"Local chunk build not available for {localVersionTag}"
                : $"Local chunk build loaded for {localVersionTag}", "Sophon");
        }

        // Include ALL manifests (game + all audio languages)
        // The install service will filter by audio need after hard-link phase
        Logger.Info($"Available manifests: {string.Join(", ", sophonBuild.Manifests.Select(m => m.MatchingField))}", "Sophon");

        int manifestIndex = 0, manifestTotal = sophonBuild.Manifests.Count;
        foreach (var manifest in sophonBuild.Manifests)
        {
            var mf = manifest.MatchingField;
            if (IsAudioManifest(mf))
            {
                if (!includeAudioManifests)
                {
                    Logger.Debug($"Skipping audio manifest: {mf}", "Sophon");
                    continue;
                }
                if (audioManifestFields is { Count: > 0 } && !audioManifestFields.Contains(mf ?? ""))
                {
                    Logger.Debug($"Skipping unselected audio manifest: {mf}", "Sophon");
                    continue;
                }
            }

            manifestIndex++;
            statusCallback?.Invoke($"正在解析清单 ({manifestIndex}/{manifestTotal}): {mf}...");
            await Task.Yield(); // let UI thread process pending renders
            Logger.Debug($"Processing manifest {manifestIndex}/{manifestTotal}: {mf} (files={manifest.Stats?.FileCount}, chunks={manifest.Stats?.ChunkCount})", "Sophon");

            var chunkManifest = await DownloadChunkManifestAsync(manifest.ManifestDownload, manifest.Manifest, ct);
            if (chunkManifest == null) continue;

            Dictionary<string, SophonChunkFile> localFiles = new(StringComparer.OrdinalIgnoreCase);
            if (localBuild?.Manifests?.FirstOrDefault(x => x.MatchingField == mf) is SophonManifestInfo localManifest)
            {
                var localChunkManifest = await DownloadChunkManifestAsync(localManifest.ManifestDownload, localManifest.Manifest, ct);
                if (localChunkManifest != null)
                {
                    foreach (var localFile in localChunkManifest.Chuncks)
                    {
                        if (!localFile.IsFolder)
                            localFiles.TryAdd(localFile.File, localFile);
                    }
                }
            }

            string urlPrefix = manifest.ChunkDownload?.UrlPrefix?.TrimEnd('/') ?? "";

            foreach (var file in chunkManifest.Chuncks)
            {
                if (file.IsFolder) continue;
                var fullPath = ResolveSafeChildPath(installPath, file.File);
                if (fullPath == null)
                {
                    Logger.Warn($"Skipped unsafe manifest path: {file.File}", "Sophon");
                    continue;
                }

                var taskFile = new ChunkDownloadFile
                {
                    RelativePath = file.File,
                    FullPath = fullPath,
                    Size = file.Size > 0 ? file.Size : file.Chunks.Sum(c => c.UncompressedSize),
                    MD5 = file.Md5,
                    ManifestField = mf ?? "",
                    InstallPath = installPath,
                };

                localFiles.TryGetValue(file.File, out var localFile);
                Dictionary<string, SophonChunk> localChunkByMD5 = new(StringComparer.OrdinalIgnoreCase);
                if (localFile?.Chunks is not null)
                {
                    foreach (var localChunk in localFile.Chunks)
                    {
                        localChunkByMD5.TryAdd(localChunk.UncompressedMd5, localChunk);
                    }
                }

                foreach (var chunk in file.Chunks)
                {
                    var taskChunk = new ChunkInfo
                    {
                        Id = chunk.Id,
                        Url = $"{urlPrefix}/{chunk.Id}",
                        Offset = chunk.Offset,
                        CompressedSize = chunk.CompressedSize,
                        UncompressedSize = chunk.UncompressedSize,
                        CompressedMD5 = chunk.CompressedMd5,
                        UncompressedMD5 = chunk.UncompressedMd5,
                    };

                    if (localFile != null &&
                        localChunkByMD5.TryGetValue(chunk.UncompressedMd5, out var localChunk) &&
                        localChunk.UncompressedSize == chunk.UncompressedSize)
                    {
                        taskChunk.OriginalFileName = localFile.File;
                        taskChunk.OriginalFileFullPath = ResolveSafeChildPath(installPath, localFile.File) ?? "";
                        taskChunk.OriginalFileSize = localFile.Size;
                        taskChunk.OriginalFileOffset = localChunk.Offset;
                    }

                    taskFile.Chunks.Add(taskChunk);
                }

                plan.Files.Add(taskFile);
            }
        }

        plan.TotalBytes = plan.Files.Sum(f => f.Chunks.Sum(c => c.CompressedSize));
        plan.TotalUncompressedBytes = plan.Files.Sum(f => f.Size);
        plan.TotalFiles = plan.Files.Count;
        plan.TotalChunks = plan.Files.Sum(f => f.Chunks.Count);

        Logger.Info($"Chunk plan: {plan.TotalFiles} files, {plan.TotalChunks} chunks, {plan.TotalBytes / (1024 * 1024)} MB compressed", "Sophon");
        return plan;
    }

    #endregion

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

    public static bool IsAudioManifest(string? matchingField)
        => !string.IsNullOrEmpty(matchingField)
           && matchingField.Contains('-')
           && matchingField.Length is 5 or 10;
}

// === Download plan models ===

public class ChunkDownloadPlan
{
    public string GameBiz { get; set; }
    public string InstallPath { get; set; }
    public string Version { get; set; }
    public List<ChunkDownloadFile> Files { get; set; } = new();
    public long TotalBytes { get; set; }
    public long TotalUncompressedBytes { get; set; }
    public int TotalFiles { get; set; }
    public int TotalChunks { get; set; }
}

public class ChunkDownloadFile
{
    public string InstallPath { get; set; }
    public string RelativePath { get; set; }
    public string FullPath { get; set; }
    public long Size { get; set; }
    public string MD5 { get; set; }
    public string ManifestField { get; set; } = "";
    public List<ChunkInfo> Chunks { get; set; } = new();
    public string HardLinkTarget { get; set; }
    public bool IsFinished { get; set; }
}

public class ChunkInfo
{
    public string Id { get; set; }
    public string Url { get; set; }
    public long Offset { get; set; }
    public long CompressedSize { get; set; }
    public long UncompressedSize { get; set; }
    public string CompressedMD5 { get; set; }
    public string UncompressedMD5 { get; set; }
    public string OriginalFileName { get; set; }
    public string OriginalFileFullPath { get; set; }
    public long OriginalFileSize { get; set; }
    public long OriginalFileOffset { get; set; }
}


