using System.Windows;
using System.Windows.Input;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenShin_Launcher_Plus.Core;
using GenShin_Launcher_Plus.Models;
using GenShin_Launcher_Plus.Service;
using GenShin_Launcher_Plus.Service.IService;

namespace GenShin_Launcher_Plus.ViewModels
{
    public class HomePageViewModel : ObservableObject
    {
        private readonly ILaunchService _launchService;
        private readonly ILauncherSession _session;
        private readonly System.Func<GameInstallService> _gameInstallServiceFactory;

        public HomePageViewModel(ILaunchService launchService, ILauncherSession session, System.Func<GameInstallService> gameInstallServiceFactory)
        {
            _launchService = launchService;
            _session = session;
            _gameInstallServiceFactory = gameInstallServiceFactory;
            RunGameCommand = new AsyncRelayCommand(_launchService.RunGameAsync);
            VerifyGameCommand = new AsyncRelayCommand(VerifyGameAsync);

            if (!string.IsNullOrEmpty(_session.Data.SwitchUser))
            {
                _session.AccountOverlay!.IsSwitchUser = Visibility.Visible;
                _session.AccountOverlay.SwitchUser = $"{languages.UserNameLab} : {UserDataService.GetAccountDisplayName(_session.Data.SwitchUser)}";
            }
            else
            {
                _session.AccountOverlay!.IsSwitchUser = Visibility.Collapsed;
            }
        }

        public LanguageModel languages => _session.Language!;
        public ICommand RunGameCommand { get; }
        public ICommand VerifyGameCommand { get; }

        private async Task VerifyGameAsync()
        {
            var biz = _session.Data.ActiveBiz;
            var profile = _session.Data.ActiveGame;
            var path = _session.Data.GamePath;
            if (profile == null || string.IsNullOrWhiteSpace(path) || !System.IO.Directory.Exists(path))
            {
                Helper.DialogHelper.ShowWarning(languages.PathErrorMessageStr, languages.Error);
                return;
            }

            var service = _gameInstallServiceFactory();
            var issues = await service.VerifyGameResourcesAsync(biz.Value, path);
            if (issues.Count == 0)
                Helper.DialogHelper.ShowInfo("资源校验完成，未发现异常文件。", languages.TipsStr);
            else
                Helper.DialogHelper.ShowWarning($"资源校验完成，发现 {issues.Count} 个异常文件，可在设置中执行修复或重新更新。", languages.Warning);
        }

    }
}
