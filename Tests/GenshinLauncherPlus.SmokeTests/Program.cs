using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using GenShin_Launcher_Plus.Helper;
using GenShin_Launcher_Plus.Models;
using GenShin_Launcher_Plus.Models.HoYoPlay;
using GenShin_Launcher_Plus.Service;

var failures = new List<string>();

void Check(string name, bool condition)
{
    if (!condition) failures.Add(name);
}

var genshinGlobal = new GameBiz("genshin_global");
Check("GameBiz parses game", genshinGlobal.Game == "genshin");
Check("GameBiz parses server", genshinGlobal.Server == "global");
Check("GameBiz identifies global", genshinGlobal.IsGlobalServer());
Check("GameBiz identifies known value", genshinGlobal.IsKnown());
Check("GameBiz rejects unknown value", !new GameBiz("unknown_cn").IsKnown());

Check("IP region recognizes China", ApiRegionService.IsChinaCountry("\u4e2d\u56fd"));
Check("IP region rejects non-China", !ApiRegionService.IsChinaCountry("United States"));
Check("CN game selects mihoyo metadata API", HoYoPlayGameMap.GetApiBaseUrl("genshin_cn").Contains("mihoyo.com", StringComparison.Ordinal));
Check("Global game selects hoyoverse metadata API", HoYoPlayGameMap.GetApiBaseUrl("genshin_global").Contains("hoyoverse.com", StringComparison.Ordinal));
Check("Global game selects hoyoverse downloader", HoYoPlayGameMap.GetSophonBaseUrl("starrail_global").Contains("hoyoverse.com", StringComparison.Ordinal));
Check("China IP selects mihoyo artwork API", HoYoPlayGameMap.GetRegionalArtworkApiBaseUrl(true).Contains("mihoyo.com", StringComparison.Ordinal));
Check("Overseas IP selects hoyoverse artwork API", HoYoPlayGameMap.GetRegionalArtworkApiBaseUrl(false).Contains("hoyoverse.com", StringComparison.Ordinal));
Check("China artwork uses matching CN game ids", HoYoPlayGameMap.GetRegionalArtworkGameBiz("genshin_global", true) == "genshin_cn");
Check("Overseas artwork uses matching global game ids", HoYoPlayGameMap.GetRegionalArtworkGameBiz("genshin_cn", false) == "genshin_global");
Check("HoYoPlay biz maps to app biz", HoYoPlayGameMap.ToAppBiz("hk4e_global") == "genshin_global");
Check("API routing normalizes game biz casing", HoYoPlayGameMap.GetApiBaseUrl("Genshin_Global").Contains("hoyoverse.com", StringComparison.Ordinal));

foreach (var game in new[] { "genshin", "starrail", "zzz", "honkai3" })
{
    var cnBiz = $"{game}_cn";
    var globalBiz = $"{game}_global";
    Check($"{game} CN metadata stays on mihoyo", HoYoPlayGameMap.GetApiBaseUrl(cnBiz).Contains("mihoyo.com", StringComparison.Ordinal));
    Check($"{game} global metadata stays on hoyoverse", HoYoPlayGameMap.GetApiBaseUrl(globalBiz).Contains("hoyoverse.com", StringComparison.Ordinal));
    Check($"{game} global Sophon stays on hoyoverse", HoYoPlayGameMap.GetSophonBaseUrl(globalBiz).Contains("hoyoverse.com", StringComparison.Ordinal));
    Check($"{game} CN launcher id matches artwork host", HoYoPlayGameMap.GetLauncherId(cnBiz) == "jGHBHlcOq1");
    Check($"{game} global launcher id matches artwork host", HoYoPlayGameMap.GetLauncherId(globalBiz) == "VYTpXlbWo8");
    Check($"{game} API ids are mapped", !string.IsNullOrWhiteSpace(HoYoPlayGameMap.GetApiGameId(cnBiz)) && !string.IsNullOrWhiteSpace(HoYoPlayGameMap.GetApiGameId(globalBiz)));
}

const string globalContentFixture = """
{"retcode":0,"message":"OK","data":{"content":{"banners":[{"image":{"url":"https://example.com/banner.jpg","link":"https://example.com/event"}}],"posts":[{"id":"1","type":"POST_TYPE_ACTIVITY","title":"Global post","link":"https://example.com/post","date":"2026-08-20"}]}}}
""";
var parsedContent = JsonSerializer.Deserialize<HoYoApiResponse<GameContentResponse>>(globalContentFixture);
Check("Global content fixture parses banner", parsedContent?.Data?.Content?.Banners?[0].Image?.Url?.EndsWith("banner.jpg", StringComparison.Ordinal) == true);
Check("Global content fixture parses post", parsedContent?.Data?.Content?.Posts?[0].Title == "Global post");

const string globalBranchFixture = """
{"retcode":0,"message":"OK","data":{"game_branches":[{"game":{"id":"gopR6Cufr3","biz":"hk4e_global"},"main":{"package_id":"pkg","branch":"main","password":"secret","tag":"7.0.0","diff_tags":[],"categories":[]}}]}}
""";
var parsedBranch = JsonSerializer.Deserialize<HoYoApiResponse<GameBranchResponse>>(globalBranchFixture);
Check("Global branch fixture parses version", parsedBranch?.Data?.GameBranches?[0].Main?.Tag == "7.0.0");

var searchFixtureRoot = Path.Combine(Path.GetTempPath(), "glp-search-smoke-" + Guid.NewGuid().ToString("N"));
try
{
    var profile = GameProfiles.FindById("starrail")!;
    var unknownPath = Path.Combine(searchFixtureRoot, "Star Rail copy");
    Directory.CreateDirectory(unknownPath);
    File.WriteAllBytes(Path.Combine(unknownPath, profile.CnExeName), new byte[] { 0 });
    Check("Same-name exe without server evidence is not auto-registered",
        !GameStateService.IsGameInstalled(unknownPath, profile, new GameBiz("starrail_global")));
    Check("Explicit same-name hard-link path remains usable",
        GameStateService.IsGameInstalled(
            unknownPath,
            profile,
            new GameBiz("starrail_global"),
            trustExplicitPath: true));

    File.WriteAllText(Path.Combine(unknownPath, "config.ini"), "game_biz=hkrpg_global\n");
    Check("game_biz identifies global client", GameSearchService.DetectServerFromConfig(unknownPath) == "global");
    Check("Identified same-name global client is accepted",
        GameStateService.IsGameInstalled(unknownPath, profile, new GameBiz("starrail_global")));

    var suffixPath = Path.Combine(searchFixtureRoot, "Star Rail (cn)");
    Directory.CreateDirectory(suffixPath);
    File.WriteAllBytes(Path.Combine(suffixPath, profile.CnExeName), new byte[] { 0 });
    Check("Launcher directory suffix identifies CN client", GameSearchService.DetectServerFromConfig(suffixPath) == "cn");
}
finally
{
    try { if (Directory.Exists(searchFixtureRoot)) Directory.Delete(searchFixtureRoot, recursive: true); } catch { }
}

var commandLine = new CommandLineBuilder()
    .AppendIf("-popupwindow", true)
    .Append("-screen-width", 1920)
    .Append("-screen-height", 1080)
    .AppendIf("-unused", false)
    .Build();

Check("CommandLineBuilder includes enabled flags", commandLine.Contains("-popupwindow", StringComparison.Ordinal));
Check("CommandLineBuilder includes values", commandLine.Contains("-screen-width 1920", StringComparison.Ordinal));
Check("CommandLineBuilder excludes disabled flags", !commandLine.Contains("-unused", StringComparison.Ordinal));

Check("Install operation exposes pre-download", GameInstallOperation.PreDownload.ToString() == "PreDownload");
Check("Install operation exposes integrity verification", GameInstallOperation.Verify.ToString() == "Verify");
Check("Install operation exposes repair", GameInstallOperation.Repair.ToString() == "Repair");

var healthyVerification = new GameResourceVerificationResult { TotalFiles = 3, ValidFiles = 3 };
Check("Verification result reports healthy resources", healthyVerification.IsHealthy);
var brokenVerification = new GameResourceVerificationResult { TotalFiles = 3, ValidFiles = 1, MissingFiles = 1, InvalidFiles = 1 };
Check("Verification result reports missing or invalid resources", !brokenVerification.IsHealthy);

if (failures.Count == 0)
{
    Console.WriteLine("Smoke tests passed.");
    return 0;
}

Console.Error.WriteLine("Smoke test failures: " + string.Join(", ", failures));
return 1;
