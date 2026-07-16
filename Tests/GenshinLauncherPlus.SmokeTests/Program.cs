using System;
using System.Collections.Generic;
using GenShin_Launcher_Plus.Helper;
using GenShin_Launcher_Plus.Models;

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

if (failures.Count == 0)
{
    Console.WriteLine("Smoke tests passed.");
    return 0;
}

Console.Error.WriteLine("Smoke test failures: " + string.Join(", ", failures));
return 1;
