using System.IO;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenShin_Launcher_Plus.Models;
using GenShin_Launcher_Plus.Service;
using GenShin_Launcher_Plus.Service.IService;
using GenShin_Launcher_Plus.Helper;

namespace GenShin_Launcher_Plus.ViewModels
{
    public class UsersPageViewModel : ObservableObject
    {
        private readonly IRegistryService _registryService;
        private readonly IUserDataService _userDataService;

        public UsersPageViewModel()
        {
            _registryService = new RegistryService();
            _userDataService = new UserDataService();
            SaveUserDataCommand = new RelayCommand(SaveUserData);
            RemoveThisPageCommand = new RelayCommand(RemoveThisPage);
        }

        public bool IsSaveGameConfig { get; set; }
        public IRegistryService RegistryService => _registryService;
        public IUserDataService UserDataService => _userDataService;
        public LanguageModel languages => App.Current.Language;
        public string? Name { get; set; }

        public ICommand SaveUserDataCommand { get; }
        public ICommand RemoveThisPageCommand { get; }

        private void SaveUserData()
        {
            var profile = App.Current.DataModel.ActiveGame;
            if (profile == null) return;
            bool isGlobal = !File.Exists(Path.Combine(App.Current.DataModel.GamePath, profile.CnExeName));
            string gamePort = isGlobal ? "Global" : "CN";
            if (!string.IsNullOrEmpty(Name))
            {
                var accountName = Name.Trim();
                string? userdata = RegistryService.GetFromRegistry(accountName, gamePort, IsSaveGameConfig);
                if (string.IsNullOrWhiteSpace(userdata))
                {
                    DialogHelper.ShowWarning(languages.SaveAccountErr, languages.Error);
                    return;
                }
                Directory.CreateDirectory("UserData");
                var fileName = Service.UserDataService.BuildAccountFileName(App.Current.DataModel.ActiveGameBiz, accountName);
                File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "UserData", fileName), userdata);
                App.Current.NoticeOverAllBase.UserLists = UserDataService.ReadUserList();
                RemoveThisPage();
            }
            else
            {
                DialogHelper.ShowWarning(languages.EmptyAccountNameError, languages.Error);
            }
        }

        private void RemoveThisPage()
        {
            App.Current.ThisMainWindow.ViewModel.NavigateHomeCommand.Execute(null);
        }
    }
}