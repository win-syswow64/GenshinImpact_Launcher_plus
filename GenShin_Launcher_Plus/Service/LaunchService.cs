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
        public LaunchService()
        {
            ReadUserList();
        }

        public async Task RunGameAsync()
        {
            var biz = App.Current.DataModel.ActiveBiz;
            var profile = App.Current.DataModel.ActiveGame;
            if (profile == null)
            {
                Logger.Error("ActiveGame is null for {biz}", "Launch");
                DialogHelper.ShowWarning(App.Current.Language.PathErrorMessageStr, App.Current.Language.Error);
                return;
            }
            var gamePath = App.Current.DataModel.GamePath;

            string exeName = profile.GetExeName(biz);
            string gameMain = Path.Combine(gamePath, exeName);

            if (!File.Exists(gameMain))
            {
                Logger.Warn($"Game executable not found: {gameMain}", "Launch");
                DialogHelper.ShowWarning(App.Current.Language.PathErrorMessageStr, App.Current.Language.Error);
                return;
            }

            // Apply server config before launching (Starward-style)
            var configService = new GameConfigService(profile, biz, gamePath);
            if (!await configService.ApplyServerConfigAsync())
            {
                DialogHelper.ShowWarning("Bilibili 登录 SDK 下载失败，请检查网络后重试。", App.Current.Language.Error);
                return;
            }

            string arg = new CommandLineBuilder()
                .AppendIf("-popupwindow", App.Current.DataModel.IsPopup)
                .Append("-screen-fullscreen", App.Current.DataModel.FullSize)
                .Append("-screen-height", App.Current.DataModel.Height)
                .Append("-screen-width", App.Current.DataModel.Width)
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

            if (App.Current.DataModel.IsRunThenClose)
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
            App.Current.NoticeOverAllBase.UserLists = new UserDataService().ReadUserList()
                .FindAll(x => string.Equals(x.GameBiz, App.Current.DataModel.ActiveGameBiz, System.StringComparison.OrdinalIgnoreCase));
        }
    }
}
