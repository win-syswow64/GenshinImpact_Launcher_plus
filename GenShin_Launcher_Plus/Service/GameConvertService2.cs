using GenShin_Launcher_Plus.Core;
using GenShin_Launcher_Plus.Helper;
using GenShin_Launcher_Plus.Models;
using GenShin_Launcher_Plus.Models.HoYoPlay;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace GenShin_Launcher_Plus.Service
{
    /// <summary>
    /// Manages the game's Config.ini (cps, channel, sub_channel) and Bilibili SDK DLL.
    /// </summary>
    internal class GameConfigService
    {
        private static readonly HttpClient _httpClient = new(new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
        });

        private readonly GameProfile _profile;
        private readonly GameBiz _biz;
        private readonly string _gamePath;
        private const string DefaultSdkPkgVersionFileName = "sdk_pkg_version";
        private const string BilibiliPlatformFolderName = "BLPlatform64";

        public GameConfigService(GameProfile profile, GameBiz biz, string gamePath)
        {
            _profile = profile;
            _biz = biz;
            _gamePath = gamePath;
        }

        /// <summary>
        /// Write cps/channel/sub_channel and ensure the Bilibili login SDK is ready.
        /// </summary>
        public async Task<bool> ApplyServerConfigAsync(CancellationToken ct = default)
        {
            string configPath = Path.Combine(_gamePath, "Config.ini");
            if (!File.Exists(configPath)) return true;

            string cps;
            var (channel, subChannel) = HoYoPlayGameMap.GetChannelInfo(_biz.Value);

            if (_biz.IsBilibili())
                cps = "bilibili";
            else if (_biz.IsGlobalServer())
                cps = "hoyoverse";
            else
                cps = "mihoyo";

            var gp = new IniParser(configPath);
            gp.AddSetting("General", "cps", cps);
            gp.AddSetting("General", "channel", Convert.ToString(channel));
            gp.AddSetting("General", "sub_channel", Convert.ToString(subChannel));
            gp.SaveSettings();

            bool sdkReady = await ManageBilibiliSdkAsync(ct).ConfigureAwait(false);
            Logger.Info($"Applied server config: {_biz} -> cps={cps}, channel={channel}, sub_channel={subChannel}", "Config");
            return sdkReady;
        }

        private async Task<bool> ManageBilibiliSdkAsync(CancellationToken ct)
        {
            if (_profile?.BilibiliSdkPath == null) return true;

            string dataFolder = _profile.GetDataFolder(_biz);
            string sdkPath = Path.Combine(_gamePath, dataFolder, _profile.BilibiliSdkPath);

            if (_biz.IsBilibili())
            {
                try
                {
                    return await EnsureBilibiliSdkAsync(sdkPath, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to download Bilibili SDK: {ex}", "Config");
                    return false;
                }
            }

            if (File.Exists(sdkPath)
                || File.Exists(Path.Combine(_gamePath, DefaultSdkPkgVersionFileName))
                || Directory.Exists(Path.Combine(Path.GetDirectoryName(sdkPath) ?? "", BilibiliPlatformFolderName)))
            {
                try
                {
                    RemoveBilibiliSdkFiles(sdkPath);
                    Logger.Info($"Removed Bilibili SDK files from {Path.GetDirectoryName(sdkPath)}", "Config");
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Failed to remove Bilibili SDK: {ex.Message}", "Config");
                }
            }

            return true;
        }

        private async Task<bool> EnsureBilibiliSdkAsync(string sdkPath, CancellationToken ct)
        {
            var channelSDK = await HoYoPlayApiService.GetGameChannelSDKAsync(_biz.Value, ct).ConfigureAwait(false);
            var package = channelSDK?.ChannelSDKPackage;
            if (string.IsNullOrWhiteSpace(package?.Url))
            {
                Logger.Error($"Bilibili SDK package not found for {_biz}", "Config");
                return false;
            }

            var versionFilePath = GetSdkVersionFilePath(channelSDK!, sdkPath);
            if (await IsInstalledSdkValidAsync(channelSDK!, versionFilePath, ct).ConfigureAwait(false))
            {
                Logger.Info($"Bilibili SDK already exists: {sdkPath} ({channelSDK!.Version})", "Config");
                return true;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(sdkPath)!);
            string cachePath = await DownloadSdkPackageAsync(package, channelSDK!.Version, ct).ConfigureAwait(false);
            ExtractSdkPackage(cachePath, _gamePath);
            WriteSdkVersionToConfig(channelSDK!.Version);
            Logger.Info($"Installed Bilibili SDK to {sdkPath} ({channelSDK!.Version})", "Config");
            return await IsInstalledSdkValidAsync(channelSDK!, versionFilePath, ct).ConfigureAwait(false);
        }

        private static async Task<string> DownloadSdkPackageAsync(GameChannelSdkPackage package, string? version, CancellationToken ct)
        {
            var cacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "Cache", "channel_sdk");
            Directory.CreateDirectory(cacheDir);

            string fileName = Path.GetFileName(new Uri(package.Url).AbsolutePath);
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = $"bilibili_sdk_{version ?? "latest"}.zip";
            var cachePath = Path.Combine(cacheDir, fileName);

            if (File.Exists(cachePath) && IsDownloadedPackageValid(cachePath, package))
                return cachePath;

            var tempPath = cachePath + ".tmp";
            using var response = await _httpClient.GetAsync(package.Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await input.CopyToAsync(output, ct).ConfigureAwait(false);
            }

            if (!IsDownloadedPackageValid(tempPath, package))
            {
                TryDeleteFile(tempPath);
                throw new IOException("Bilibili SDK package verification failed.");
            }

            File.Move(tempPath, cachePath, true);
            return cachePath;
        }

        private static bool IsDownloadedPackageValid(string path, GameChannelSdkPackage package)
        {
            if (!File.Exists(path)) return false;
            var info = new FileInfo(path);
            if (package.Size > 0 && info.Length != package.Size) return false;
            return string.IsNullOrWhiteSpace(package.MD5) || VerifyFileMD5(path, package.MD5);
        }

        private static void ExtractSdkPackage(string packagePath, string installPath)
        {
            using var archive = ZipFile.OpenRead(packagePath);
            var root = Path.GetFullPath(installPath);
            if (!root.EndsWith(Path.DirectorySeparatorChar))
                root += Path.DirectorySeparatorChar;

            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.FullName)) continue;

                var fullPath = Path.GetFullPath(Path.Combine(root, entry.FullName));
                if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new IOException($"Unsafe path in Bilibili SDK package: {entry.FullName}");

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

        private static string GetSdkVersionFilePath(GameChannelSdkInfo channelSDK, string sdkPath)
        {
            string versionFileName = string.IsNullOrWhiteSpace(channelSDK.PkgVersionFileName)
                ? DefaultSdkPkgVersionFileName
                : Path.GetFileName(channelSDK.PkgVersionFileName);
            return Path.Combine(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sdkPath)!, "..", "..")), versionFileName);
        }

        private async Task<bool> IsInstalledSdkValidAsync(GameChannelSdkInfo channelSDK, string versionFilePath, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(channelSDK.Version)) return true;
            if (!string.Equals(ReadSdkVersionFromConfig(), channelSDK.Version, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!File.Exists(versionFilePath))
                return false;

            var pkgVersionItems = await ReadPkgVersionItemsAsync(versionFilePath, ct).ConfigureAwait(false);
            if (pkgVersionItems.Length == 0)
                return false;

            foreach (var item in pkgVersionItems)
            {
                var file = ResolveSafeChildPath(_gamePath, item.RemoteName);
                if (file == null || !VerifyFile(file, item.FileSize, item.MD5))
                    return false;
            }

            return true;
        }

        private string? ReadSdkVersionFromConfig()
        {
            var configPath = Path.Combine(_gamePath, "Config.ini");
            if (!File.Exists(configPath)) return null;
            var gp = new IniParser(configPath);
            return gp.GetSetting("General", "sdk_version", 0);
        }

        private void WriteSdkVersionToConfig(string? version)
        {
            var configPath = Path.Combine(_gamePath, "Config.ini");
            if (!File.Exists(configPath)) return;
            var gp = new IniParser(configPath);
            gp.AddSetting("General", "sdk_version", version ?? "");
            gp.SaveSettings();
        }

        private static async Task<PkgVersionItem[]> ReadPkgVersionItemsAsync(string path, CancellationToken ct)
        {
            var lines = await File.ReadAllLinesAsync(path, ct).ConfigureAwait(false);
            return lines
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x =>
                {
                    try { return JsonSerializer.Deserialize<PkgVersionItem>(x); }
                    catch { return null; }
                })
                .Where(x => x != null)
                .Cast<PkgVersionItem>()
                .ToArray();
        }

        private void RemoveBilibiliSdkFiles(string sdkPath)
        {
            var pkgVersionPath = Path.Combine(_gamePath, DefaultSdkPkgVersionFileName);
            if (File.Exists(pkgVersionPath))
            {
                foreach (var item in ReadPkgVersionItemsAsync(pkgVersionPath, CancellationToken.None).GetAwaiter().GetResult())
                {
                    var path = ResolveSafeChildPath(_gamePath, item.RemoteName);
                    if (path != null)
                        TryDeleteFile(path);
                }
                TryDeleteFile(pkgVersionPath);
            }

            TryDeleteFile(sdkPath);

            var pluginsDir = Path.GetDirectoryName(sdkPath);
            if (!string.IsNullOrWhiteSpace(pluginsDir))
            {
                var platformDir = Path.Combine(pluginsDir, BilibiliPlatformFolderName);
                if (Directory.Exists(platformDir))
                    Directory.Delete(platformDir, true);
            }

            WriteSdkVersionToConfig("");
        }

        private static bool VerifyFileMD5(string path, string expectedMD5)
        {
            using var md5 = MD5.Create();
            using var fs = File.OpenRead(path);
            var hash = Convert.ToHexString(md5.ComputeHash(fs));
            return string.Equals(hash, expectedMD5, StringComparison.OrdinalIgnoreCase);
        }

        private static bool VerifyFile(string path, long expectedSize, string expectedMD5)
        {
            if (!File.Exists(path)) return false;
            if (expectedSize > 0 && new FileInfo(path).Length != expectedSize) return false;
            return string.IsNullOrWhiteSpace(expectedMD5) || VerifyFileMD5(path, expectedMD5);
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

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch { }
        }

        /// <summary>
        /// Read the current cps value from Config.ini.
        /// </summary>
        public static string ReadCps(string gamePath)
        {
            string configPath = Path.Combine(gamePath, "Config.ini");
            if (!File.Exists(configPath)) return null;
            var gp = new IniParser(configPath);
            return gp.GetSetting("General", "cps", 0);
        }
    }

    internal class PkgVersionItem
    {
        [JsonPropertyName("remoteName")]
        public string RemoteName { get; set; }

        [JsonPropertyName("md5")]
        public string MD5 { get; set; }

        [JsonPropertyName("fileSize")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public long FileSize { get; set; }
    }
}
