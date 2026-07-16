using System;
using System.Collections.Generic;
using GenShin_Launcher_Plus.Helper;
using GenShin_Launcher_Plus.Models;
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
