using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using GenShin_Launcher_Plus.Core;
using GenShin_Launcher_Plus.Helper;
using GenShin_Launcher_Plus.Models;
using GenShin_Launcher_Plus.Service.IService;

namespace GenShin_Launcher_Plus.Service
{
    public class LaunchService : ILaunchService
    {
        private readonly ILauncherSession _session;
        private readonly IUserDataService _userDataService;

        public LaunchService(ILauncherSession session, IUserDataService userDataService)
        {
            _session = session;
            _userDataService = userDataService;
        }

        public async Task RunGameAsync()
        {
            var biz = _session.Data.ActiveBiz;
            var profile = _session.Data.ActiveGame;
            if (profile == null)
            {
                Logger.Error("ActiveGame is null for {biz}", "Launch");
                DialogHelper.ShowWarning(_session.Language!.PathErrorMessageStr, _session.Language.Error);
                return;
            }
            // Do not launch through the legacy per-game fallback: it may be
            // another server's client after the user switches GameBiz.
            var gamePath = _session.Data.GetExactGamePath(biz.Value);
            if (string.IsNullOrWhiteSpace(gamePath))
            {
                Logger.Warn($"No exact install path configured for {biz}", "Launch");
                DialogHelper.ShowWarning(_session.Language!.PathErrorMessageStr, _session.Language.Error);
                return;
            }

            string exeName = profile.GetExeName(biz);
            string gameMain = Path.Combine(gamePath, exeName);

            if (!File.Exists(gameMain))
            {
                Logger.Warn($"Game executable not found: {gameMain}", "Launch");
                DialogHelper.ShowWarning(_session.Language!.PathErrorMessageStr, _session.Language.Error);
                return;
            }

            // Apply server config before launching (Starward-style)
            var configService = new GameConfigService(profile, biz, gamePath);
            if (!await configService.ApplyServerConfigAsync())
            {
                DialogHelper.ShowWarning("Bilibili 登录 SDK 下载失败，请检查网络后重试。", _session.Language!.Error);
                return;
            }

            string arg = new CommandLineBuilder()
                .AppendIf("-popupwindow", _session.Data.IsPopup)
                .Append("-screen-fullscreen", _session.Data.FullSize)
                .Append("-screen-height", _session.Data.Height)
                .Append("-screen-width", _session.Data.Width)
                .ToString();

            Logger.Info($"Starting game: {gameMain} ({biz}) with args: {arg}", "Launch");
            Application.Current.MainWindow.WindowState = WindowState.Minimized;

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = gameMain,
                    Verb = "runas",
                    UseShellExecute = true,
                    WorkingDirectory = gamePath,
                    Arguments = arg,
                }
            };

            bool started = process.Start();

            if (_session.Data.IsRunThenClose)
            {
                Logger.Info("Game started, exiting launcher (run-then-close)", "Launch");
                Environment.Exit(0);
            }
            else if (started)
            {
                Logger.Debug("Waiting for game process to exit", "Launch");
                await process.WaitForExitAsync();
                Logger.Info("Game process exited", "Launch");
                Application.Current.MainWindow.WindowState = WindowState.Normal;
            }
            else
            {
                Logger.Error($"Failed to start game process: {gameMain}", "Launch");
            }
        }

        public void ReadUserList()
        {
            _session.AccountOverlay!.UserLists = _userDataService.ReadUserList()
                .FindAll(x => string.Equals(x.GameBiz, _session.Data.ActiveGameBiz, System.StringComparison.OrdinalIgnoreCase));
        }
    }
}
