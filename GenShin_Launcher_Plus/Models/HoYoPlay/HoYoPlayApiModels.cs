using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GenShin_Launcher_Plus.Models.HoYoPlay;

// === API Response Wrappers ===

public class HoYoApiResponse<T>
{
    [JsonPropertyName("retcode")]
    public long Retcode { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; }

    [JsonPropertyName("data")]
    public T Data { get; set; }
}

// === Game Package ===

public class GamePackageResponse
{
    [JsonPropertyName("game_packages")]
    public List<GamePackageInfo> GamePackages { get; set; }
}

public class GamePackageInfo
{
    [JsonPropertyName("game")]
    public GameIdInfo Game { get; set; }

    [JsonPropertyName("main")]
    public GamePackageVersionInfo Main { get; set; }

    [JsonPropertyName("pre_download")]
    public GamePackageVersionInfo PreDownload { get; set; }
}

public class GameIdInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("biz")]
    public string Biz { get; set; }
}

public class GamePackageVersionInfo
{
    [JsonPropertyName("major")]
    public GamePackageResourceInfo Major { get; set; }

    [JsonPropertyName("patches")]
    public List<GamePackageResourceInfo> Patches { get; set; }
}

public class GamePackageResourceInfo
{
    [JsonPropertyName("version")]
    public string Version { get; set; }

    [JsonPropertyName("game_pkgs")]
    public List<GamePackageFileInfo> GamePackages { get; set; }

    [JsonPropertyName("audio_pkgs")]
    public List<GamePackageFileInfo> AudioPackages { get; set; }

    [JsonPropertyName("res_list_url")]
    public string ResListUrl { get; set; }
}

public class GamePackageFileInfo
{
    [JsonPropertyName("url")]
    public string Url { get; set; }

    [JsonPropertyName("md5")]
    public string MD5 { get; set; }

    [JsonPropertyName("size")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public long Size { get; set; }

    [JsonPropertyName("decompressed_size")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public long DecompressedSize { get; set; }
}

// === Game Branch (for chunk download mode) ===

public class GameBranchResponse
{
    [JsonPropertyName("game_branches")]
    public List<GameBranchInfo> GameBranches { get; set; }
}

public class GameBranchInfo
{
    [JsonPropertyName("game")]
    public GameIdInfo Game { get; set; }

    [JsonPropertyName("main")]
    public GameBranchPackageInfo Main { get; set; }

    [JsonPropertyName("pre_download")]
    public GameBranchPackageInfo PreDownload { get; set; }
}

public class GameBranchPackageInfo
{
    [JsonPropertyName("package_id")]
    public string PackageId { get; set; }

    [JsonPropertyName("branch")]
    public string Branch { get; set; }

    [JsonPropertyName("password")]
    public string Password { get; set; }

    [JsonPropertyName("tag")]
    public string Tag { get; set; }

    [JsonPropertyName("diff_tags")]
    public List<string> DiffTags { get; set; }
}

// === Game Config ===

public class GameConfigResponse
{
    [JsonPropertyName("launch_configs")]
    public List<GameLaunchConfig> LaunchConfigs { get; set; }
}

public class GameLaunchConfig
{
    [JsonPropertyName("game")]
    public GameIdInfo Game { get; set; }

    [JsonPropertyName("exe_file_name")]
    public string ExeFileName { get; set; }

    [JsonPropertyName("installation_dir")]
    public string InstallationDir { get; set; }

    [JsonPropertyName("default_download_mode")]
    public string DefaultDownloadMode { get; set; }
}

// === Game Channel SDK ===

public class GameChannelSdkResponse
{
    [JsonPropertyName("game_channel_sdks")]
    public List<GameChannelSdkInfo> GameChannelSDKs { get; set; }
}

public class GameChannelSdkInfo
{
    [JsonPropertyName("game")]
    public GameIdInfo Game { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; }

    [JsonPropertyName("channel_sdk_pkg")]
    public GameChannelSdkPackage ChannelSDKPackage { get; set; }

    [JsonPropertyName("pkg_version_file_name")]
    public string PkgVersionFileName { get; set; }
}

public class GameChannelSdkPackage
{
    [JsonPropertyName("url")]
    public string Url { get; set; }

    [JsonPropertyName("md5")]
    public string MD5 { get; set; }

    [JsonPropertyName("size")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public long Size { get; set; }

    [JsonPropertyName("decompressed_size")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public long DecompressedSize { get; set; }
}

// === Sophon Chunk Build (from downloader API) ===

public class SophonChunkBuildResponse
{
    [JsonPropertyName("build_id")]
    public string BuildId { get; set; }

    [JsonPropertyName("tag")]
    public string Tag { get; set; }

    [JsonPropertyName("manifests")]
    public List<SophonManifestInfo> Manifests { get; set; }
}

public class SophonManifestInfo
{
    [JsonPropertyName("category_id")]
    public string CategoryId { get; set; }

    [JsonPropertyName("category_name")]
    public string CategoryName { get; set; }

    [JsonPropertyName("matching_field")]
    public string MatchingField { get; set; }

    [JsonPropertyName("manifest")]
    public SophonManifestFileInfo Manifest { get; set; }

    [JsonPropertyName("chunk_download")]
    public SophonDownloadUrl ChunkDownload { get; set; }

    [JsonPropertyName("manifest_download")]
    public SophonDownloadUrl ManifestDownload { get; set; }

    [JsonPropertyName("stats")]
    public SophonStats Stats { get; set; }

    [JsonPropertyName("deduplicated_stats")]
    public SophonStats DeduplicatedStats { get; set; }
}

public class SophonManifestFileInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("checksum")]
    public string Checksum { get; set; }

    [JsonPropertyName("compressed_size")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public long CompressedSize { get; set; }

    [JsonPropertyName("uncompressed_size")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public long UncompressedSize { get; set; }
}

public class SophonDownloadUrl
{
    [JsonPropertyName("encryption")]
    public int Encryption { get; set; }

    [JsonPropertyName("password")]
    public string Password { get; set; }

    [JsonPropertyName("compression")]
    public int Compression { get; set; }

    [JsonPropertyName("url_prefix")]
    public string UrlPrefix { get; set; }

    [JsonPropertyName("url_suffix")]
    public string UrlSuffix { get; set; }
}

public class SophonStats
{
    [JsonPropertyName("compressed_size")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public long CompressedSize { get; set; }

    [JsonPropertyName("uncompressed_size")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public long UncompressedSize { get; set; }

    [JsonPropertyName("file_count")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public int FileCount { get; set; }

    [JsonPropertyName("chunk_count")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public int ChunkCount { get; set; }
}
