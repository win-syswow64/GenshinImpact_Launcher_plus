using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
using Microsoft.Win32;

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
            InstallService = new GameInstallService();

            // Propagate install service property changes to our derived properties
            InstallService.PropertyChanged += (_, e) =>
            {
                switch (e.PropertyName)
                {
                    case nameof(GameInstallService.IsInstalling):
                    case nameof(GameInstallService.State):
                    case nameof(GameInstallService.StateText):
                        OnPropertyChanged(nameof(ActionButtonText));
                        OnPropertyChanged(nameof(ActionButtonEnabled));
                        OnPropertyChanged(nameof(ShowProgressPanel));
                        OnPropertyChanged(nameof(ShowPreDownloadButton));
                        break;
                    case nameof(GameInstallService.ProgressPercent):
                    case nameof(GameInstallService.ProgressText):
                    case nameof(GameInstallService.DownloadSpeedText):
                    case nameof(GameInstallService.BytesProgressText):
                        // These bind directly, but also update button text
                        OnPropertyChanged(nameof(ActionButtonText));
                        break;
                }
            };

            NavigateHomeCommand = new RelayCommand(() => NavigateTo(new HomePage()));
            NavigateSettingsCommand = new RelayCommand(() => NavigateTo(new SettingPage()));
            NavigateUsersCommand = new RelayCommand(() => NavigateTo(new UsersPage()));
            NavigateProgramSettingsCommand = new RelayCommand(() => NavigateTo(new SettingPage(3)));
            OpenGameSettingsCommand = new RelayCommand(() => NavigateTo(new SettingPage(0)));
            RunGameCommand = new AsyncRelayCommand(RunGameOrInstallAsync);
            SelectGameCommand = new RelayCommand<string>(SelectGame);
            InstallGameCommand = new AsyncRelayCommand(InstallGameAsync);
            UpdateGameCommand = new AsyncRelayCommand(UpdateGameAsync);
            PreDownloadCommand = new AsyncRelayCommand(PreDownloadAsync);
            CancelInstallCommand = new RelayCommand(CancelInstall);

            Title = $"{languages.MainTitle} {Application.ResourceAssembly.GetName().Version}";
            App.Current.DataModel.EXEname(Path.GetFileName(Environment.ProcessPath));

            _ = SetNoticeAsync();
            _ = RefreshGameStateAsync();
        }

        public IMainWindowService MainService { get; }
        public LanguageModel languages => App.Current.Language;

        // === Game Install Service (exposed for XAML binding) ===
        public GameInstallService InstallService { get; }

        private GameStateInfo _gameState;
        public GameStateInfo CurrentGameState
        {
            get => _gameState;
            set
            {
                SetProperty(ref _gameState, value);
                OnPropertyChanged(nameof(ActionButtonText));
                OnPropertyChanged(nameof(ActionButtonEnabled));
                OnPropertyChanged(nameof(ShowInstallPanel));
                OnPropertyChanged(nameof(ShowProgressPanel));
                OnPropertyChanged(nameof(CanRunGame));
                OnPropertyChanged(nameof(ShowPreDownloadButton));
            }
        }

        // === Action button display ===
        public string ActionButtonText
        {
            get
            {
                if (InstallService.IsInstalling)
                    return InstallService.StateText;
                if (CurrentGameState == null)
                    return languages.RunGameBtn ?? "启动游戏";
                return CurrentGameState.State switch
                {
                    GameState.NotInstalled => languages.InstallGameBtn ?? "安装游戏",
                    GameState.NeedUpdate => languages.UpdateGameBtn ?? "更新游戏",
                    _ => languages.RunGameBtn ?? "启动游戏",
                };
            }
        }

        public bool ActionButtonEnabled =>
            !InstallService.IsInstalling &&
            CurrentGameState?.State != GameState.Running;

        public bool ShowInstallPanel =>
            CurrentGameState?.State == GameState.NotInstalled && !InstallService.IsInstalling;

        public bool ShowProgressPanel => InstallService.IsInstalling;

        public bool ShowPreDownloadButton =>
            CurrentGameState?.State == GameState.PreDownloadAvailable && !InstallService.IsInstalling;

        public ICommand InstallGameCommand { get; }
        public ICommand UpdateGameCommand { get; }
        public ICommand PreDownloadCommand { get; }
        public ICommand CancelInstallCommand { get; }

        private async Task RunGameOrInstallAsync()
        {
            if (CurrentGameState == null) return;
            switch (CurrentGameState.State)
            {
                case GameState.NotInstalled:
                    await InstallGameAsync();
                    break;
                case GameState.NeedUpdate:
                    await UpdateGameAsync();
                    break;
                default:
                    await _launchService.RunGameAsync();
                    break;
            }
        }

        private async Task InstallGameAsync()
        {
            var gameBiz = App.Current.DataModel.ActiveGameBiz;
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = languages.SelectInstallPathText ?? "选择安装路径",
                ShowNewFolderButton = true,
            };

            // Find drives with existing game installations for hard link support
            var existingDrives = GameInstallService.GetExistingGameDrives();
            var defaultDir = GameInstallService.GetDefaultInstallDir(gameBiz);
            dialog.SelectedPath = defaultDir;

            // Show hint about hard link requirement
            if (existingDrives.Count > 0)
            {
                string driveList = string.Join(", ", existingDrives);
                Logger.Info($"Existing game drives: {driveList} - install must be on same drive for hard link", "Install");
            }

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var installPath = dialog.SelectedPath;
                var installDrive = Path.GetPathRoot(installPath);

                // Enforce same-drive constraint for hard link
                if (existingDrives.Count > 0 && !existingDrives.Any(d => string.Equals(d, installDrive, StringComparison.OrdinalIgnoreCase)))
                {
                    string driveList = string.Join(", ", existingDrives);
                    DialogHelper.ShowWarning(
                        $"安装路径必须位于已有游戏所在的盘符 ({driveList}) 以启用硬链接加速。\n\n硬链接可以共享相同资源文件，大幅减少下载量。\n\n请选择 {driveList} 盘下的路径。",
                        languages.TipsStr ?? "提示");
                    return;
                }

                // Validate NTFS
                try
                {
                    var drive = new DriveInfo(installDrive!);
                    if (!drive.DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase))
                    {
                        DialogHelper.ShowWarning("安装路径必须位于 NTFS 文件系统的分区上。", languages.TipsStr ?? "提示");
                        return;
                    }
                }
                catch { }

                App.Current.DataModel.SetGamePath(gameBiz, installPath);
                OnPropertyChanged(nameof(GamePathDisplay));
                await InstallService.InstallGameAsync(gameBiz, installPath);
                await RefreshGameStateAsync();
            }
        }

        private async Task UpdateGameAsync()
        {
            var gameBiz = App.Current.DataModel.ActiveGameBiz;
            var installPath = App.Current.DataModel.GamePath;
            if (string.IsNullOrEmpty(installPath)) return;
            await InstallService.UpdateGameAsync(gameBiz, installPath);
            await RefreshGameStateAsync();
        }

        private async Task PreDownloadAsync()
        {
            var gameBiz = App.Current.DataModel.ActiveGameBiz;
            var installPath = App.Current.DataModel.GamePath;
            if (string.IsNullOrEmpty(installPath)) return;
            await InstallService.PreDownloadAsync(gameBiz, installPath);
            await RefreshGameStateAsync();
        }

        private void CancelInstall()
        {
            InstallService.Pause();
        }

        // === Refresh game state ===
        private System.Threading.CancellationTokenSource? _gameStateCts;

        public async Task RefreshGameStateAsync()
        {
            _gameStateCts?.Cancel();
            _gameStateCts = new System.Threading.CancellationTokenSource();
            var token = _gameStateCts.Token;
            try
            {
                var gameBiz = App.Current.DataModel.ActiveGameBiz;
                CurrentGameState = await GameStateService.DetectGameStateAsync(gameBiz, token);
            }
            catch (TaskCanceledException) { }
            catch (Exception ex) { Logger.Warn($"RefreshGameState failed: {ex.Message}", "GameState"); }
        }

        // === Standard properties ===
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

        public Visibility NavScreenshotsVisible => App.Current.DataModel.NavScreenshots ? Visibility.Visible : Visibility.Collapsed;
        public Visibility NavQQGroupVisible => App.Current.DataModel.NavQQGroup ? Visibility.Visible : Visibility.Collapsed;
        public Visibility NavAboutVisible => App.Current.DataModel.NavAbout ? Visibility.Visible : Visibility.Collapsed;

        public List<GameProfile> GameList => GameProfiles.All;

        public int SelectedGameIndex
        {
            get
            {
                var current = App.Current.DataModel.ActiveBiz.Game;
                for (int i = 0; i < GameProfiles.All.Count; i++)
                    if (GameProfiles.All[i].Id == current) return i;
                for (int i = 0; i < GameProfiles.All.Count; i++)
                    if (GameProfiles.All[i].IsInstalled) return i;
                return 0;
            }
            set
            {
                if (value < 0 || value >= GameProfiles.All.Count) return;
                var newGame = GameProfiles.All[value];
                var currentBiz = App.Current.DataModel.ActiveBiz;
                if (newGame.Id == currentBiz.Game) return;
                string newBizStr = $"{newGame.Id}_{currentBiz.Server}";
                var newBiz = new GameBiz(newBizStr);
                if (!newBiz.IsKnown()) newBizStr = $"{newGame.Id}_cn";
                App.Current.DataModel.ActiveGameBiz = newBizStr;
                App.Current.DataModel.SelectedGame = newGame.Id;
                Logger.Info($"Game switched to: {newGame.DisplayName} ({newBizStr})", "App");
                RefreshGameSelector();
            }
        }

        public List<string> ServerList
        {
            get
            {
                var profile = App.Current.DataModel.ActiveGame;
                var servers = new List<string> { "cn", "global" };
                if (profile?.BilibiliSdkPath != null) servers.Add("bilibili");
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
            SwitchPort = $"{languages.GameClientStr} : {CurrentServerDisplay}";
            App.Current.NoticeOverAllBase.SwitchPort = SwitchPort;
            _ = ReloadBackgroundAsync();
            _ = RefreshGameStateAsync();
        }

        private System.Threading.CancellationTokenSource? _bgCts;
        private async Task ReloadBackgroundAsync()
        {
            _bgCts?.Cancel();
            _bgCts = new System.Threading.CancellationTokenSource();
            var token = _bgCts.Token;
            try
            {
                await Task.Delay(200, token);
                await Service.MainService.LoadGameBackgroundAsync();
            }
            catch (TaskCanceledException) { }
            catch (Exception ex) { Logger.Warn("Background reload failed: " + ex.Message, "Background"); }
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
                if (CurrentGameState?.State == GameState.NotInstalled) return false;
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
        private void MainMinimized() => _main.WindowState = WindowState.Minimized;
        private void OpenImagesDirectory() => NavigateTo(new ScreenshotsPage());
        private void OpenAbout()
        {
            var result = DialogHelper.ShowYesNo(languages.AboutStr + "\n\nOpen GitHub?", languages.AboutTitle);
            if (result) FileHelper.OpenUrl("https://github.com/win-syswow64/GenshinImpact_Luncher_plus");
        }
        private async Task SetNoticeAsync()
        {
            await MainService.CheckNotice();
            if (App.Current.NoticeObject?.Code == 200)
                DialogHelper.ShowInfo(App.Current.NoticeObject.NoticeMsg, languages.TipsStr);
        }
    }
}




