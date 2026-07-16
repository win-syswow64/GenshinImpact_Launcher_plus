using System.Windows;
using System.Windows.Input;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenShin_Launcher_Plus.Models;
using GenShin_Launcher_Plus.Service;
using GenShin_Launcher_Plus.Service.IService;

namespace GenShin_Launcher_Plus.ViewModels
{
    public class HomePageViewModel : ObservableObject
    {
        private readonly ILaunchService _launchService;

        public HomePageViewModel()
        {
            _launchService = new LaunchService();
            RunGameCommand = new AsyncRelayCommand(_launchService.RunGameAsync);
            VerifyGameCommand = new AsyncRelayCommand(VerifyGameAsync);
            OpenSettingsCommand = new RelayCommand(OpenSettings);

            if (!string.IsNullOrEmpty(App.Current.DataModel.SwitchUser))
            {
                App.Current.NoticeOverAllBase.IsSwitchUser = Visibility.Visible;
                App.Current.NoticeOverAllBase.SwitchUser = $"{languages.UserNameLab} : {UserDataService.GetAccountDisplayName(App.Current.DataModel.SwitchUser)}";
            }
            else
            {
                App.Current.NoticeOverAllBase.IsSwitchUser = Visibility.Collapsed;
            }
        }

        public LanguageModel languages => App.Current.Language;
        public ICommand RunGameCommand { get; }
        public ICommand VerifyGameCommand { get; }
        public ICommand OpenSettingsCommand { get; }

        private async Task VerifyGameAsync()
        {
            var biz = App.Current.DataModel.ActiveBiz;
            var profile = App.Current.DataModel.ActiveGame;
            var path = App.Current.DataModel.GamePath;
            if (profile == null || string.IsNullOrWhiteSpace(path) || !System.IO.Directory.Exists(path))
            {
                Helper.DialogHelper.ShowWarning(languages.PathErrorMessageStr, languages.Error);
                return;
            }

            var service = new GameInstallService();
            var issues = await service.VerifyGameResourcesAsync(biz.Value, path);
            if (issues.Count == 0)
                Helper.DialogHelper.ShowInfo("资源校验完成，未发现异常文件。", languages.TipsStr);
            else
                Helper.DialogHelper.ShowWarning($"资源校验完成，发现 {issues.Count} 个异常文件，可在设置中执行修复或重新更新。", languages.Warning);
        }

        private void OpenSettings()
        {
            App.Current.ThisMainWindow.ViewModel.NavigateTo(new Views.SettingPage());
        }
    }
}
