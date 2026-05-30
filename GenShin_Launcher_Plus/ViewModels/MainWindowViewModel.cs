using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenShin_Launcher_Plus.Helper;
using GenShin_Launcher_Plus.Models;
using GenShin_Launcher_Plus.Service;
using GenShin_Launcher_Plus.Service.IService;
using GenShin_Launcher_Plus.Views;

namespace GenShin_Launcher_Plus.ViewModels
{
    public class MainWindowViewModel : ObservableObject
    {
        private readonly MainWindow _main;
        private readonly ILaunchService _launchService;

        public MainWindowViewModel(MainWindow main)
        {
            _main = main;
            App.Current.LoadProgramCore.LoadLanguageCore();
            new UpdateService().CheckUpdate(main);
            MainService = new MainService(main, this);

            ExitProgramCommand = new RelayCommand(ExitProgram);
            MainMinimizedCommand = new RelayCommand(MainMinimized);
            OpenAboutCommand = new RelayCommand(OpenAbout);
            OpenQQGroupUrlCommand = new RelayCommand(() => FileHelper.OpenUrl("https://qm.qq.com/q/UZWuLb38om"));
            OpenImagesDirectoryCommand = new RelayCommand(OpenImagesDirectory);
            _launchService = new LaunchService();

            NavigateHomeCommand = new RelayCommand(() => NavigateTo(new HomePage()));
            NavigateSettingsCommand = new RelayCommand(() => NavigateTo(new SettingPage()));
            NavigateUsersCommand = new RelayCommand(() => NavigateTo(new UsersPage()));
            NavigateProgramSettingsCommand = new RelayCommand(() => NavigateTo(new SettingPage(3)));
            OpenGameSettingsCommand = new RelayCommand(() => NavigateTo(new SettingPage(0)));
            RunGameCommand = new AsyncRelayCommand(_launchService.RunGameAsync);
            SelectGameCommand = new RelayCommand<string>(SelectGame);

            Title = $"{languages.MainTitle} {Application.ResourceAssembly.GetName().Version}";
            App.Current.DataModel.EXEname(Path.GetFileName(Environment.ProcessPath));

            _ = SetNoticeAsync();
        }

        public IMainWindowService MainService { get; }
        public LanguageModel languages => App.Current.Language;

        private string _title = string.Empty;
        public string Title { get => _title; set => SetProperty(ref _title, value); }

        private ImageBrush _background = new();
        public ImageBrush Background { get => _background; set => SetProperty(ref _background, value); }

        private object? _currentPage;
        public object? CurrentPage { get => _currentPage; set => SetProperty(ref _currentPage, value); }

        private string _switchUser = string.Empty;
        public string SwitchUser { get => _switchUser; set => SetProperty(ref _switchUser, value); }

        private string _switchPort = string.Empty;
        public string SwitchPort { get => _switchPort; set => SetProperty(ref _switchPort, value); }

        private Visibility _isSwitchUser = Visibility.Collapsed;
        public Visibility IsSwitchUser { get => _isSwitchUser; set => SetProperty(ref _isSwitchUser, value); }

        public ICommand ExitProgramCommand { get; }
        public ICommand MainMinimizedCommand { get; }
        public ICommand OpenAboutCommand { get; }
        public ICommand OpenQQGroupUrlCommand { get; }
        public ICommand OpenImagesDirectoryCommand { get; }
        public ICommand NavigateHomeCommand { get; }
        public ICommand NavigateSettingsCommand { get; }
        public ICommand NavigateUsersCommand { get; }
        public ICommand NavigateProgramSettingsCommand { get; }
        public ICommand OpenGameSettingsCommand { get; }
        public ICommand RunGameCommand { get; }
        public ICommand SelectGameCommand { get; }

        // Nav feature visibility
        public Visibility NavScreenshotsVisible => App.Current.DataModel.NavScreenshots ? Visibility.Visible : Visibility.Collapsed;
        public Visibility NavQQGroupVisible => App.Current.DataModel.NavQQGroup ? Visibility.Visible : Visibility.Collapsed;
        public Visibility NavAboutVisible => App.Current.DataModel.NavAbout ? Visibility.Visible : Visibility.Collapsed;

        // === Game selector ===
        public List<GameProfile> GameList => GameProfiles.All;

        public int SelectedGameIndex
        {
            get
            {
                var current = App.Current.DataModel.ActiveBiz.Game;
                for (int i = 0; i < GameProfiles.All.Count; i++)
                    if (GameProfiles.All[i].Id == current) return i;
                return 0;
            }
            set
            {
                if (value < 0 || value >= GameProfiles.All.Count) return;
                var newGame = GameProfiles.All[value];
                var currentBiz = App.Current.DataModel.ActiveBiz;
                if (newGame.Id == currentBiz.Game) return;

                // Switch to the same server type for the new game, or default to CN
                string newBizStr = $"{newGame.Id}_{currentBiz.Server}";
                var newBiz = new GameBiz(newBizStr);
                if (!newBiz.IsKnown()) newBizStr = $"{newGame.Id}_cn";

                App.Current.DataModel.ActiveGameBiz = newBizStr;
                App.Current.DataModel.SelectedGame = newGame.Id;
                Logger.Info($"Game switched to: {newGame.DisplayName} ({newBizStr})", "App");
                RefreshGameSelector();
            }
        }

        // === Server selector (Starward-style) ===
        public List<string> ServerList
        {
            get
            {
                var profile = App.Current.DataModel.ActiveGame;
                var servers = new List<string>();
                servers.Add("cn");
                servers.Add("global");
                if (profile?.BilibiliSdkPath != null)
                    servers.Add("bilibili");
                return servers;
            }
        }

        public List<string> ServerDisplayNames
        {
            get
            {
                var list = new List<string>();
                foreach (var s in ServerList)
                    list.Add(s switch
                    {
                        "cn" => languages.GameClientTypePStr ?? "CN",
                        "global" => languages.GameClientTypeMStr ?? "Global",
                        "bilibili" => languages.GameClientTypeBStr ?? "Bilibili",
                        _ => s,
                    });
                return list;
            }
        }

        public int SelectedServerIndex
        {
            get
            {
                var server = App.Current.DataModel.ActiveBiz.Server;
                var list = ServerList;
                for (int i = 0; i < list.Count; i++)
                    if (list[i] == server) return i;
                return 0;
            }
            set
            {
                var list = ServerList;
                if (value < 0 || value >= list.Count) return;
                var newServer = list[value];
                var currentBiz = App.Current.DataModel.ActiveBiz;
                if (newServer == currentBiz.Server) return;

                string newBizStr = $"{currentBiz.Game}_{newServer}";
                App.Current.DataModel.ActiveGameBiz = newBizStr;
                Logger.Info($"Server switched to: {newBizStr}", "App");
                RefreshGameSelector();
            }
        }

        public string CurrentServerDisplay
        {
            get
            {
                var biz = App.Current.DataModel.ActiveBiz;
                if (biz.IsChinaServer()) return languages.GameClientTypePStr;
                if (biz.IsGlobalServer()) return languages.GameClientTypeMStr;
                if (biz.IsBilibili()) return languages.GameClientTypeBStr;
                return "";
            }
        }

        public string GamePathDisplay => App.Current.DataModel.GamePath ?? string.Empty;

        private void SelectGame(string? gameId)
        {
            if (string.IsNullOrEmpty(gameId)) return;
            var idx = GameProfiles.All.FindIndex(p => p.Id == gameId);
            if (idx >= 0) SelectedGameIndex = idx;
        }

        public void RefreshGameSelector()
        {
            OnPropertyChanged(nameof(SelectedGameIndex));
            OnPropertyChanged(nameof(SelectedServerIndex));
            OnPropertyChanged(nameof(ServerList));
            OnPropertyChanged(nameof(ServerDisplayNames));
            OnPropertyChanged(nameof(GamePathDisplay));
            OnPropertyChanged(nameof(CanRunGame));
            OnPropertyChanged(nameof(CurrentServerDisplay));
            // Update the SwitchPort display on the main window
            SwitchPort = $"{languages.GameClientStr} : {CurrentServerDisplay}";
            App.Current.NoticeOverAllBase.SwitchPort = SwitchPort;
        }

        public void RefreshNavVisibility()
        {
            OnPropertyChanged(nameof(NavScreenshotsVisible));
            OnPropertyChanged(nameof(NavQQGroupVisible));
            OnPropertyChanged(nameof(NavAboutVisible));
        }

        public bool CanRunGame
        {
            get
            {
                var path = App.Current.DataModel.GamePath;
                if (string.IsNullOrEmpty(path)) return false;
                var biz = App.Current.DataModel.ActiveBiz;
                var game = App.Current.DataModel.ActiveGame;
                if (game == null) return false;
                string exe = game.GetExeName(biz);
                return File.Exists(Path.Combine(path, exe));
            }
        }

        public Visibility LaunchPanelVisible => CurrentPage == null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility OverlayVisible => CurrentPage != null ? Visibility.Visible : Visibility.Collapsed;

        public void NavigateTo(object? page)
        {
            CurrentPage = page;
            OnPropertyChanged(nameof(LaunchPanelVisible));
            OnPropertyChanged(nameof(OverlayVisible));
            OnPropertyChanged(nameof(CanRunGame));
        }

        private void ExitProgram()
        {
            var result = DialogHelper.ShowYesNo(languages.ExitConfirmMessage, languages.TipsStr);
            if (result) Environment.Exit(0);
        }

        private void MainMinimized()
        {
            _main.WindowState = WindowState.Minimized;
        }

        private void OpenImagesDirectory()
        {
            NavigateTo(new ScreenshotsPage());
        }

        private void OpenAbout()
        {
            var result = DialogHelper.ShowYesNo(languages.AboutStr + "\n\nOpen GitHub?", languages.AboutTitle);
            if (result) FileHelper.OpenUrl("https://github.com/win-syswow64/GenshinImpact_Luncher_plus");
        }

        private async Task SetNoticeAsync()
        {
            await MainService.CheckNotice();
            if (App.Current.NoticeObject?.Code == 200)
            {
                DialogHelper.ShowInfo(App.Current.NoticeObject.NoticeMsg, languages.TipsStr);
            }
        }
    }
}