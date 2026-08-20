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
        public HomePageViewModel(ILaunchService launchService, ILauncherSession session)
        {
            _launchService = launchService;
            _session = session;
            RunGameCommand = new AsyncRelayCommand(_launchService.RunGameAsync);

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
    }
}
