using System.Windows;
using System.Windows.Input;
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
            OpenSettingsCommand = new RelayCommand(OpenSettings);

            if (!string.IsNullOrEmpty(App.Current.DataModel.SwitchUser))
            {
                App.Current.NoticeOverAllBase.IsSwitchUser = Visibility.Visible;
                App.Current.NoticeOverAllBase.SwitchUser = $"{languages.UserNameLab} : {App.Current.DataModel.SwitchUser}";
            }
            else
            {
                App.Current.NoticeOverAllBase.IsSwitchUser = Visibility.Collapsed;
            }
        }

        public LanguageModel languages => App.Current.Language;
        public ICommand RunGameCommand { get; }
        public ICommand OpenSettingsCommand { get; }

        private void OpenSettings()
        {
            App.Current.ThisMainWindow.ViewModel.NavigateTo(new Views.SettingPage());
        }
    }
}
