using System.Collections.Generic;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using GenShin_Launcher_Plus.Models;

namespace GenShin_Launcher_Plus.Service
{
    public class NoticeOverAllBase : ObservableObject
    {
        private string _switchUser = string.Empty;
        public string SwitchUser
        {
            get => _switchUser;
            set => SetProperty(ref _switchUser, value);
        }

        private string _switchPort = string.Empty;
        public string SwitchPort
        {
            get
            {
                var biz = App.Current.DataModel.ActiveBiz;
                string gameClientType = biz.IsBilibili() ? App.Current.Language.GameClientTypeBStr
                    : biz.IsGlobalServer() ? App.Current.Language.GameClientTypeMStr
                    : App.Current.Language.GameClientTypePStr;
                return $"{App.Current.Language.GameClientStr} : {gameClientType}";
            }
            set => SetProperty(ref _switchPort, value);
        }

        private int _gamePortListIndex;
        public int GamePortListIndex
        {
            get
            {
                var biz = App.Current.DataModel.ActiveBiz;
                int index = biz.IsBilibili() ? 1 : biz.IsGlobalServer() ? 2 : 0;
                string gameClientType = biz.IsBilibili() ? App.Current.Language.GameClientTypeBStr
                    : biz.IsGlobalServer() ? App.Current.Language.GameClientTypeMStr
                    : App.Current.Language.GameClientTypePStr;
                SwitchPort = $"{App.Current.Language.GameClientStr} : {gameClientType}";
                return index;
            }
            set => SetProperty(ref _gamePortListIndex, value);
        }

        private string? _switchUserValue;
        public string? SwitchUserValue
        {
            get => _switchUserValue;
            set
            {
                SetProperty(ref _switchUserValue, value);
                if (!string.IsNullOrEmpty(SwitchUserValue))
                {
                    var account = UserLists.FirstOrDefault(x => x.UserName == SwitchUserValue);
                    if (account == null || !string.Equals(account.GameBiz, App.Current.DataModel.ActiveGameBiz, System.StringComparison.OrdinalIgnoreCase))
                        return;
                    SwitchUser = $"{App.Current.Language.UserNameLab} : {account.DisplayName}";
                    App.Current.DataModel.SwitchUser = SwitchUserValue;
                    App.Current.DataModel.SaveDataToFile();
                    new RegistryService().SetToRegistry(SwitchUserValue);
                }
            }
        }

        private List<GamePortListModel> _gamePortLists = new();
        public List<GamePortListModel> GamePortLists
        {
            get => _gamePortLists;
            set => SetProperty(ref _gamePortLists, value);
        }

        private Visibility _isGamePortLists = Visibility.Visible;
        public Visibility IsGamePortLists
        {
            get
            {
                _isGamePortLists = App.Current.DataModel.ActiveBiz.IsGlobalServer()
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                return _isGamePortLists;
            }
            set => SetProperty(ref _isGamePortLists, value);
        }

        private Visibility _isSwitchUser = Visibility.Collapsed;
        public Visibility IsSwitchUser
        {
            get => _isSwitchUser;
            set => SetProperty(ref _isSwitchUser, value);
        }

        private List<UserListModel> _userLists = new();
        public List<UserListModel> UserLists
        {
            get => _userLists;
            set => SetProperty(ref _userLists, value);
        }
    }
}