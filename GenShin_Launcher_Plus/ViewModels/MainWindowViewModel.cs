using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenShin_Launcher_Plus.Core;
using GenShin_Launcher_Plus.Helper;
using GenShin_Launcher_Plus.Models;
using GenShin_Launcher_Plus.Models.HoYoPlay;
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
        private readonly ILauncherSession _session;
        private readonly GameServerSwitchService _serverSwitchService;
        private readonly Func<MainWindow, MainWindowViewModel, IMainWindowService> _mainServiceFactory;
        private readonly ILauncherNavigationService _navigationService;
        private readonly DispatcherTimer _bannerTimer;

        public MainWindowViewModel(
            MainWindow main,
            IUpdateService updateService,
            ILaunchService launchService,
            LoadProgramCore loadProgramCore,
            ILauncherSession session,
            GameInstallService installService,
            GameServerSwitchService serverSwitchService,
            Func<MainWindow, MainWindowViewModel, IMainWindowService> mainServiceFactory,
            ILauncherNavigationService navigationService)
        {
            _main = main;
            _session = session;
            _serverSwitchService = serverSwitchService;
            _mainServiceFactory = mainServiceFactory;
            _navigationService = navigationService;
            _bannerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _bannerTimer.Tick += (_, _) => NextGameBanner();
            _navigationService.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ILauncherNavigationService.CurrentPage))
                {
                    OnPropertyChanged(nameof(CurrentPage));
                    OnPropertyChanged(nameof(OverlayVisible));
                    OnPropertyChanged(nameof(CanRunGame));
                }
            };
            loadProgramCore.LoadLanguageCore();
            updateService.CheckUpdate(main);
            MainService = _mainServiceFactory(main, this);

            ExitProgramCommand = new RelayCommand(ExitProgram);
            MainMinimizedCommand = new RelayCommand(MainMinimized);
            MainMaximizedCommand = new RelayCommand(ToggleWindowState);
            OpenAboutCommand = new RelayCommand(OpenAbout);
            OpenQQGroupUrlCommand = new RelayCommand(() => FileHelper.OpenUrl("https://qm.qq.com/q/UZWuLb38om"));
            OpenImagesDirectoryCommand = new RelayCommand(OpenImagesDirectory);
            ToggleSidebarCommand = new RelayCommand(ToggleSidebar);
            _launchService = launchService;
            _launchService.ReadUserList();
            InstallService = installService;

            // Propagate install service property changes to our derived properties
            InstallService.PropertyChanged += (_, e) =>
            {
                switch (e.PropertyName)
                {
                    case nameof(GameInstallService.IsInstalling):
                    case nameof(GameInstallService.IsTaskVisible):
                    case nameof(GameInstallService.CanPause):
                    case nameof(GameInstallService.CanContinue):
                    case nameof(GameInstallService.State):
                    case nameof(GameInstallService.StateText):
                    case nameof(GameInstallService.Operation):
                    case nameof(GameInstallService.OperationText):
                        OnPropertyChanged(nameof(ActionButtonText));
                        OnPropertyChanged(nameof(ActionButtonEnabled));
                        OnPropertyChanged(nameof(ShowProgressPanel));
                        OnPropertyChanged(nameof(ShowPreDownloadButton));
                        OnPropertyChanged(nameof(InstallStatusTitle));
                        OnPropertyChanged(nameof(InstallControlText));
                        OnPropertyChanged(nameof(CanToggleInstallPause));
                        OnPropertyChanged(nameof(InstallDetailText));
                        OnPropertyChanged(nameof(CanVerifyGame));
                        OnPropertyChanged(nameof(CanRepairGame));
                        OnPropertyChanged(nameof(CanChangeGameSelection));
                        OnPropertyChanged(nameof(VersionStatusText));
                        break;
                    case nameof(GameInstallService.ProgressPercent):
                    case nameof(GameInstallService.ProgressText):
                    case nameof(GameInstallService.DownloadSpeedText):
                    case nameof(GameInstallService.BytesProgressText):
                    case nameof(GameInstallService.RemainingTimeText):
                    case nameof(GameInstallService.VerificationSummary):
                    case nameof(GameInstallService.ErrorText):
                        // These bind directly, but also update button text
                        OnPropertyChanged(nameof(ActionButtonText));
                        OnPropertyChanged(nameof(InstallStatusTitle));
                        OnPropertyChanged(nameof(InstallDetailText));
                        break;
                }
            };

            NavigateHomeCommand = new RelayCommand(NavigateHome);
            NavigateSettingsCommand = new RelayCommand(_navigationService.NavigateGameSettings);
            NavigateUsersCommand = new RelayCommand(_navigationService.NavigateUsers);
            NavigateProgramSettingsCommand = new RelayCommand(_navigationService.NavigateProgramSettings);
            OpenGameSettingsCommand = new RelayCommand(_navigationService.NavigateGameSettings);
            RunGameCommand = new AsyncRelayCommand(RunGameOrInstallAsync);
            SelectGameCommand = new RelayCommand<string>(SelectGame);
            InstallGameCommand = new AsyncRelayCommand(InstallGameAsync);
            UpdateGameCommand = new AsyncRelayCommand(UpdateGameAsync);
            PreDownloadCommand = new AsyncRelayCommand(PreDownloadAsync);
            CancelInstallCommand = new RelayCommand(CancelInstall);
            ContinueInstallCommand = new AsyncRelayCommand(ContinueInstallAsync);
            ToggleInstallPauseCommand = new AsyncRelayCommand(ToggleInstallPauseAsync);
            VerifyGameCommand = new AsyncRelayCommand(VerifyGameAsync);
            RepairGameCommand = new AsyncRelayCommand(RepairGameAsync);
            OpenGameNewsCommand = new RelayCommand<GameNewsItem>(OpenGameNews);
            OpenGameBannerCommand = new RelayCommand(OpenGameBanner);
            PreviousGameBannerCommand = new RelayCommand(PreviousGameBanner);
            NextGameBannerCommand = new RelayCommand(NextGameBanner);
            SelectGameNewsCategoryCommand = new RelayCommand<string>(SelectGameNewsCategory);
            RefreshGameNewsCommand = new AsyncRelayCommand(RefreshGameNewsAsync);
            languages.PropertyChanged += (_, _) =>
            {
                Title = "GenShin Launcher Plus";
                OnPropertyChanged(nameof(ActionButtonText));
                OnPropertyChanged(nameof(ServerDisplayNames));
                OnPropertyChanged(nameof(CurrentServerDisplay));
                OnPropertyChanged(nameof(CurrentGameDisplayName));
                OnPropertyChanged(nameof(VersionStatusText));
                OnPropertyChanged(nameof(ProgramVersionText));
                OnPropertyChanged(nameof(InstallStatusTitle));
            };

            Title = "GenShin Launcher Plus";
            _session.Data.EXEname(Path.GetFileName(Environment.ProcessPath));
            NavigateHome();

            _ = RefreshGameNewsAsync();
            _ = RefreshGameStateAsync();
        }

        public IMainWindowService MainService { get; }
        public LanguageModel languages => _session.Language!;

        // === Game Install Service (exposed for XAML binding) ===
        public GameInstallService InstallService { get; }

        private GameStateInfo? _gameState;
        public GameStateInfo? CurrentGameState
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
                OnPropertyChanged(nameof(VersionStatusText));
                OnPropertyChanged(nameof(InstallStatusTitle));
                OnPropertyChanged(nameof(CanVerifyGame));
                OnPropertyChanged(nameof(CanRepairGame));
                OnPropertyChanged(nameof(CanChangeGameSelection));
            }
        }

        private bool _isGameStateLoading;
        public bool IsGameStateLoading
        {
            get => _isGameStateLoading;
            private set
            {
                if (SetProperty(ref _isGameStateLoading, value))
                {
                    OnPropertyChanged(nameof(ActionButtonText));
                    OnPropertyChanged(nameof(ActionButtonEnabled));
                    OnPropertyChanged(nameof(VersionStatusText));
                    OnPropertyChanged(nameof(CanChangeGameSelection));
                }
            }
        }

        private string _gameStateErrorText = string.Empty;
        public string GameStateErrorText
        {
            get => _gameStateErrorText;
            private set
            {
                if (SetProperty(ref _gameStateErrorText, value))
                {
                    OnPropertyChanged(nameof(VersionStatusText));
                    OnPropertyChanged(nameof(ActionButtonText));
                    OnPropertyChanged(nameof(ActionButtonEnabled));
                }
            }
        }

        // === Action button display ===
        public string ActionButtonText
        {
            get
            {
                if (InstallService.IsInstalling)
                    return InstallService.StateText;
                if (InstallService.CanContinue)
                    return $"继续{InstallService.OperationText}";
                if (IsGameStateLoading)
                    return "正在检查…";
                if (CurrentGameState == null)
                    return string.IsNullOrWhiteSpace(GameStateErrorText) ? "暂不可用" : "重试检查";
                return CurrentGameState.State switch
                {
                    GameState.NotInstalled => languages.InstallGameBtn ?? "安装游戏",
                    GameState.NeedUpdate => languages.UpdateGameBtn ?? "更新游戏",
                    _ => languages.RunGameBtn ?? "启动游戏",
                };
            }
        }

        public bool ActionButtonEnabled =>
            !IsGameStateLoading && !InstallService.IsInstalling && !IsSwitchingServer &&
            (CurrentGameState?.State != GameState.Running) &&
            (CurrentGameState != null || !string.IsNullOrWhiteSpace(GameStateErrorText));

        public bool ShowInstallPanel =>
            CurrentGameState?.State == GameState.NotInstalled && !InstallService.IsTaskVisible;

        public bool ShowProgressPanel => InstallService.IsTaskVisible;

        public bool ShowPreDownloadButton =>
            CurrentGameState?.State == GameState.PreDownloadAvailable && !InstallService.IsTaskVisible;

        public string CurrentGameDisplayName =>
            _session.Data.ActiveGame?.DisplayName ?? "HoYoPlay";

        public string VersionStatusText
        {
            get
            {
                if (IsGameStateLoading)
                    return "正在检查游戏状态…";
                if (!string.IsNullOrWhiteSpace(GameStateErrorText))
                    return GameStateErrorText;
                if (CurrentGameState == null)
                    return "";

                var local = CurrentGameState.LocalVersion?.ToString();
                var latest = CurrentGameState.LatestVersion;
                var pre = CurrentGameState.PreDownloadVersion;

                return CurrentGameState.State switch
                {
                    GameState.NotInstalled => languages.GameNotInstalledText ?? "游戏未安装",
                    GameState.NeedUpdate when !string.IsNullOrWhiteSpace(local) && !string.IsNullOrWhiteSpace(latest) =>
                        $"版本 {local} -> {latest}",
                    GameState.NeedUpdate => languages.UpdateGameBtn ?? "更新游戏",
                    GameState.PreDownloadAvailable when !string.IsNullOrWhiteSpace(pre) =>
                        $"可预下载 {pre}",
                    GameState.Ready when !string.IsNullOrWhiteSpace(local) =>
                        $"版本 {local}",
                    _ => languages.RunGameBtn ?? "启动游戏",
                };
            }
        }

        public string InstallStatusTitle => InstallService.IsTaskVisible
            ? InstallService.OperationText
            : VersionStatusText;

        public string InstallDetailText
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(InstallService.ErrorText))
                    return InstallService.ErrorText;
                if (!string.IsNullOrWhiteSpace(InstallService.VerificationSummary) && InstallService.Operation == GameInstallOperation.Verify)
                    return InstallService.VerificationSummary;
                if (!string.IsNullOrWhiteSpace(InstallService.BytesProgressText) || !string.IsNullOrWhiteSpace(InstallService.DownloadSpeedText))
                {
                    var remaining = string.IsNullOrWhiteSpace(InstallService.RemainingTimeText)
                        ? ""
                        : $"  剩余 {InstallService.RemainingTimeText}";
                    return $"{InstallService.BytesProgressText}  {InstallService.DownloadSpeedText}{remaining}".Trim();
                }
                return InstallService.StatusText;
            }
        }

        public string InstallControlText => InstallService.CanContinue ? "继续" : "暂停";
        public bool CanToggleInstallPause => InstallService.CanPause || InstallService.CanContinue;
        public bool CanVerifyGame => !InstallService.IsTaskVisible &&
            CurrentGameState?.State is GameState.Ready or GameState.NeedUpdate or GameState.PreDownloadAvailable;
        public bool CanRepairGame => !InstallService.IsTaskVisible &&
            CurrentGameState?.State is GameState.Ready or GameState.NeedUpdate or GameState.PreDownloadAvailable;
        public bool CanChangeGameSelection => !InstallService.IsTaskVisible && !IsSwitchingServer && !IsGameStateLoading;

        public ICommand InstallGameCommand { get; }
        public ICommand UpdateGameCommand { get; }
        public ICommand PreDownloadCommand { get; }
        public ICommand CancelInstallCommand { get; }
        public ICommand ContinueInstallCommand { get; }
        public ICommand ToggleInstallPauseCommand { get; }
        public ICommand VerifyGameCommand { get; }
        public ICommand RepairGameCommand { get; }

        private async Task RunGameOrInstallAsync()
        {
            if (InstallService.CanContinue)
            {
                await ContinueInstallAsync();
                return;
            }
            if (CurrentGameState == null)
            {
                await RefreshGameStateAsync();
                return;
            }
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
            var gameBiz = _session.Data.ActiveGameBiz;
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = languages.SelectInstallPathText ?? "选择安装路径",
                ShowNewFolderButton = true,
            };

            // Find drives with existing game installations for hard link support
            var existingDrives = GameInstallService.GetExistingGameDrives(_session.Data);
            var defaultDir = GameInstallService.GetDefaultInstallDir(_session.Data, gameBiz);
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

                if (!GameInstallService.TryValidateInstallTarget(_session.Data, gameBiz, installPath, out var installPathError))
                {
                    DialogHelper.ShowWarning(installPathError ?? "安装目录不可用。", languages.TipsStr ?? "提示");
                    return;
                }
                _session.Data.SetGamePath(gameBiz, installPath);
                OnPropertyChanged(nameof(GamePathDisplay));
                await InstallService.InstallGameAsync(gameBiz, installPath);
                await RefreshGameStateAsync();
            }
        }

        private async Task UpdateGameAsync()
        {
            var gameBiz = _session.Data.ActiveGameBiz;
            var installPath = _session.Data.GetExactGamePath(gameBiz);
            if (string.IsNullOrEmpty(installPath)) return;
            await InstallService.UpdateGameAsync(gameBiz, installPath);
            await RefreshGameStateAsync();
        }

        private async Task PreDownloadAsync()
        {
            var gameBiz = _session.Data.ActiveGameBiz;
            var installPath = _session.Data.GetExactGamePath(gameBiz);
            if (string.IsNullOrEmpty(installPath)) return;
            await InstallService.PreDownloadAsync(gameBiz, installPath);
            await RefreshGameStateAsync();
        }

        private void CancelInstall()
        {
            InstallService.Pause();
        }

        private async Task ContinueInstallAsync()
        {
            await InstallService.ContinueAsync();
            await RefreshGameStateAsync();
        }

        private async Task ToggleInstallPauseAsync()
        {
            if (InstallService.CanContinue)
                await ContinueInstallAsync();
            else if (InstallService.CanPause)
                CancelInstall();
        }

        private async Task VerifyGameAsync()
        {
            var gameBiz = _session.Data.ActiveGameBiz;
            var installPath = _session.Data.GetExactGamePath(gameBiz);
            if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
            {
                DialogHelper.ShowWarning(languages.PathErrorMessageStr, languages.Error);
                return;
            }

            var issues = await InstallService.VerifyGameResourcesAsync(gameBiz, installPath);
            if (InstallService.State == GameInstallState.Finished)
            {
                if (issues.Count == 0)
                    DialogHelper.ShowInfo("资源校验完成，未发现异常文件。", languages.TipsStr);
                else if (DialogHelper.ShowYesNo($"资源校验完成，发现 {issues.Count} 个异常文件。是否立即修复？", languages.TipsStr))
                    await RepairGameAsync();
            }
        }

        private async Task RepairGameAsync()
        {
            var gameBiz = _session.Data.ActiveGameBiz;
            var installPath = _session.Data.GetExactGamePath(gameBiz);
            if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
            {
                DialogHelper.ShowWarning(languages.PathErrorMessageStr, languages.Error);
                return;
            }

            await InstallService.RepairGameAsync(gameBiz, installPath);
            await RefreshGameStateAsync();
        }

        // === Refresh game state ===
        private System.Threading.CancellationTokenSource? _gameStateCts;

        public async Task RefreshGameStateAsync()
        {
            _gameStateCts?.Cancel();
            var refresh = new System.Threading.CancellationTokenSource();
            _gameStateCts = refresh;
            var token = refresh.Token;
            IsGameStateLoading = true;
            GameStateErrorText = string.Empty;
            CurrentGameState = null;
            try
            {
                var gameBiz = _session.Data.ActiveGameBiz;
                var state = await Task.Run(
                    async () => await GameStateService.DetectGameStateAsync(_session.Data, gameBiz, token).ConfigureAwait(false),
                    token);
                token.ThrowIfCancellationRequested();
                CurrentGameState = state;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex)
            {
                GameStateErrorText = "游戏状态检查失败";
                Logger.Warn($"RefreshGameState failed: {ex.Message}", "GameState");
            }
            finally
            {
                if (ReferenceEquals(_gameStateCts, refresh))
                    IsGameStateLoading = false;
            }
        }

        // === Standard properties ===
        private string _title = string.Empty;
        public string Title { get => _title; set => SetProperty(ref _title, value); }
        public string ProgramVersionText => $"v{Application.ResourceAssembly.GetName().Version}";
        private bool _isSidebarExpanded;
        public bool IsSidebarExpanded
        {
            get => _isSidebarExpanded;
            private set
            {
                if (SetProperty(ref _isSidebarExpanded, value))
                {
                    OnPropertyChanged(nameof(SidebarWidth));
                    OnPropertyChanged(nameof(SidebarButtonWidth));
                }
            }
        }
        public double SidebarWidth => IsSidebarExpanded ? 220 : 70;
        public double SidebarButtonWidth => IsSidebarExpanded ? 204 : 54;
        private ImageBrush _background = new();
        public ImageBrush Background { get => _background; set => SetProperty(ref _background, value); }
        public object? CurrentPage => _navigationService.CurrentPage;
        private string _switchUser = string.Empty;
        public string SwitchUser { get => _switchUser; set => SetProperty(ref _switchUser, value); }
        private string _switchPort = string.Empty;
        public string SwitchPort { get => _switchPort; set => SetProperty(ref _switchPort, value); }
        private Visibility _isSwitchUser = Visibility.Collapsed;
        public Visibility IsSwitchUser { get => _isSwitchUser; set => SetProperty(ref _isSwitchUser, value); }

        public ICommand ExitProgramCommand { get; }
        public ICommand MainMinimizedCommand { get; }
        public ICommand MainMaximizedCommand { get; }
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
        public ICommand ToggleSidebarCommand { get; }
        public ICommand OpenGameNewsCommand { get; }
        public ICommand OpenGameBannerCommand { get; }
        public ICommand PreviousGameBannerCommand { get; }
        public ICommand NextGameBannerCommand { get; }
        public ICommand SelectGameNewsCategoryCommand { get; }
        public ICommand RefreshGameNewsCommand { get; }

        public Visibility NavScreenshotsVisible => _session.Data.NavScreenshots ? Visibility.Visible : Visibility.Collapsed;
        public Visibility NavQQGroupVisible => _session.Data.NavQQGroup ? Visibility.Visible : Visibility.Collapsed;
        public Visibility NavAboutVisible => _session.Data.NavAbout ? Visibility.Visible : Visibility.Collapsed;

        public string ActiveAccountDisplay
        {
            get
            {
                var account = _session.Data.SwitchUser;
                return string.IsNullOrWhiteSpace(account)
                    ? (languages.AccountLabel ?? "未选择账号")
                    : UserDataService.GetAccountDisplayName(account);
            }
        }

        private List<GameBannerItem> _gameBanners = new();
        public List<GameBannerItem> GameBanners
        {
            get => _gameBanners;
            private set
            {
                if (SetProperty(ref _gameBanners, value))
                    OnPropertyChanged(nameof(HasMultipleBanners));
            }
        }
        private int _currentGameBannerIndex;
        public GameBannerItem? CurrentGameBanner => GameBanners.Count == 0 ? null : GameBanners[_currentGameBannerIndex % GameBanners.Count];
        public string GameBannerPosition => GameBanners.Count > 1 ? $"{_currentGameBannerIndex + 1}/{GameBanners.Count}" : "";
        public bool HasMultipleBanners => GameBanners.Count > 1;

        private List<GameNewsItem> _activityNews = new();
        public List<GameNewsItem> ActivityNews { get => _activityNews; private set => SetProperty(ref _activityNews, value); }
        private List<GameNewsItem> _announcementNews = new();
        public List<GameNewsItem> AnnouncementNews { get => _announcementNews; private set => SetProperty(ref _announcementNews, value); }
        private List<GameNewsItem> _informationNews = new();
        public List<GameNewsItem> InformationNews { get => _informationNews; private set => SetProperty(ref _informationNews, value); }
        private System.Threading.CancellationTokenSource? _newsCts;
        private bool _isNewsLoading;
        public bool IsNewsLoading
        {
            get => _isNewsLoading;
            private set
            {
                if (SetProperty(ref _isNewsLoading, value))
                {
                    OnPropertyChanged(nameof(NewsStatusText));
                    OnPropertyChanged(nameof(ShowNewsStatus));
                }
            }
        }
        private string _newsErrorText = string.Empty;
        public string NewsErrorText
        {
            get => _newsErrorText;
            private set
            {
                if (SetProperty(ref _newsErrorText, value))
                {
                    OnPropertyChanged(nameof(NewsStatusText));
                    OnPropertyChanged(nameof(ShowNewsStatus));
                }
            }
        }
        private string _selectedNewsCategory = "活动";
        public string SelectedNewsCategory
        {
            get => _selectedNewsCategory;
            private set
            {
                if (SetProperty(ref _selectedNewsCategory, value))
                {
                    OnPropertyChanged(nameof(DisplayedGameNews));
                    OnPropertyChanged(nameof(IsActivityNewsSelected));
                    OnPropertyChanged(nameof(IsAnnouncementNewsSelected));
                    OnPropertyChanged(nameof(IsInformationNewsSelected));
                    OnPropertyChanged(nameof(HasDisplayedGameNews));
                    OnPropertyChanged(nameof(ShowNewsStatus));
                    OnPropertyChanged(nameof(NewsStatusText));
                }
            }
        }
        public List<GameNewsItem> DisplayedGameNews => SelectedNewsCategory switch
        {
            "活动" => ActivityNews,
            "公告" => AnnouncementNews,
            _ => InformationNews,
        };
        public bool IsActivityNewsSelected => SelectedNewsCategory == "活动";
        public bool IsAnnouncementNewsSelected => SelectedNewsCategory == "公告";
        public bool IsInformationNewsSelected => SelectedNewsCategory == "资讯";
        public bool HasDisplayedGameNews => DisplayedGameNews.Count > 0;
        public bool ShowNewsStatus => IsNewsLoading || !HasDisplayedGameNews;
        public string NewsStatusText => IsNewsLoading
            ? "正在获取资讯…"
            : !string.IsNullOrWhiteSpace(NewsErrorText) ? NewsErrorText : "暂无资讯";

        public List<GameProfile> GameList => GameProfiles.All;

        public int SelectedGameIndex
        {
            get
            {
                var current = _session.Data.ActiveBiz.Game;
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
                var currentBiz = _session.Data.ActiveBiz;
                if (newGame.Id == currentBiz.Game) return;
                string newBizStr = $"{newGame.Id}_{currentBiz.Server}";
                var newBiz = new GameBiz(newBizStr);
                if (!newBiz.IsKnown()) newBizStr = $"{newGame.Id}_cn";
                _session.Data.ActiveGameBiz = newBizStr;
                _session.Data.SelectedGame = newGame.Id;
                Logger.Info($"Game switched to: {newGame.DisplayName} ({newBizStr})", "App");
                RefreshGameSelector();
            }
        }

        public List<string> ServerList
        {
            get
            {
                var profile = _session.Data.ActiveGame;
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
                var server = _session.Data.ActiveBiz.Server;
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
                var currentBiz = _session.Data.ActiveBiz;
                if (newServer == currentBiz.Server) return;
                string newBizStr = $"{currentBiz.Game}_{newServer}";
                _ = SwitchServerAsync(newBizStr);
            }
        }

        private bool _isSwitchingServer;
        public bool IsSwitchingServer
        {
            get => _isSwitchingServer;
            private set
            {
                if (SetProperty(ref _isSwitchingServer, value))
                {
                    OnPropertyChanged(nameof(ActionButtonEnabled));
                    OnPropertyChanged(nameof(CanChangeGameSelection));
                }
            }
        }

        private async Task SwitchServerAsync(string targetGameBiz)
        {
            if (IsSwitchingServer) return;

            IsSwitchingServer = true;
            try
            {
                var result = _serverSwitchService.Switch(targetGameBiz);
                if (result.Status == ServerSwitchStatus.Invalid)
                {
                    DialogHelper.ShowWarning(result.Message ?? "无法切换服务器。", languages.TipsStr ?? "提示");
                    return;
                }

                RefreshGameSelector(reloadBackground: false);
                await RefreshGameStateAsync();

                if (result.Status == ServerSwitchStatus.InstallRequired)
                {
                    var hardLinkHint = string.IsNullOrWhiteSpace(result.HardLinkSourcePath)
                        ? "未找到可复用的同游戏客户端，将按目标服务器下载资源。"
                        : $"检测到可复用客户端：{result.HardLinkSourcePath}。请选择同一 NTFS 分区中的新目录，安装时会自动创建硬链接以复用资源。";
                    DialogHelper.ShowWarning($"目标服务器尚未安装。\n建议目录：{result.RecommendedInstallPath}\n\n{hardLinkHint}", languages.TipsStr ?? "提示");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Server switch failed: {ex}", "ServerSwitch");
                DialogHelper.ShowWarning($"切换服务器失败：{ex.Message}", languages.TipsStr ?? "提示");
            }
            finally
            {
                IsSwitchingServer = false;
                // Restore the bound selection after a rejected/failed switch.
                OnPropertyChanged(nameof(SelectedServerIndex));
            }
        }

        public string CurrentServerDisplay
        {
            get
            {
                var biz = _session.Data.ActiveBiz;
                if (biz.IsChinaServer()) return languages.GameClientTypePStr;
                if (biz.IsGlobalServer()) return languages.GameClientTypeMStr;
                if (biz.IsBilibili()) return languages.GameClientTypeBStr;
                return "";
            }
        }

        public string GamePathDisplay => _session.Data.GetExactGamePath(_session.Data.ActiveGameBiz) ?? string.Empty;

        private void SelectGame(string? gameId)
        {
            if (string.IsNullOrEmpty(gameId)) return;
            var idx = GameProfiles.All.FindIndex(p => p.Id == gameId);
            if (idx >= 0) SelectedGameIndex = idx;
        }

        public void RefreshGameSelector(bool reloadBackground = true)
        {
            OnPropertyChanged(nameof(SelectedGameIndex));
            OnPropertyChanged(nameof(SelectedServerIndex));
            OnPropertyChanged(nameof(ServerList));
            OnPropertyChanged(nameof(ServerDisplayNames));
            OnPropertyChanged(nameof(GamePathDisplay));
            OnPropertyChanged(nameof(CanRunGame));
            OnPropertyChanged(nameof(CurrentServerDisplay));
            OnPropertyChanged(nameof(CurrentGameDisplayName));
            OnPropertyChanged(nameof(ActiveAccountDisplay));
            OnPropertyChanged(nameof(VersionStatusText));
            OnPropertyChanged(nameof(InstallStatusTitle));
            SwitchPort = $"{languages.GameClientStr} : {CurrentServerDisplay}";
            _session.AccountOverlay!.SwitchPort = SwitchPort;
            if (CurrentPage is SettingPage settingPage)
                settingPage.RefreshForActiveGame();
            if (reloadBackground)
                _ = ReloadBackgroundAsync();
            _ = RefreshGameNewsAsync();
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
                await MainService.LoadGameBackgroundAsync(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
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
                var path = _session.Data.GetExactGamePath(_session.Data.ActiveGameBiz);
                if (string.IsNullOrEmpty(path)) return false;
                var biz = _session.Data.ActiveBiz;
                var game = _session.Data.ActiveGame;
                if (game == null) return false;
                string exe = game.GetExeName(biz);
                return File.Exists(Path.Combine(path, exe));
            }
        }

        public Visibility OverlayVisible => CurrentPage is HomePage ? Visibility.Collapsed : Visibility.Visible;

        public void NavigateHome() => _navigationService.NavigateHome();

        private void ToggleSidebar() => IsSidebarExpanded = !IsSidebarExpanded;

        private void OpenGameNews(GameNewsItem? item)
        {
            if (item != null)
                OpenExternalLink(item.Link);
        }

        private void OpenGameBanner()
        {
            if (CurrentGameBanner != null)
                OpenExternalLink(CurrentGameBanner.Link);
        }

        private static void OpenExternalLink(string link)
        {
            if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) ||
                uri.Scheme is not "http" and not "https")
                return;
            FileHelper.OpenUrl(uri.AbsoluteUri);
        }

        private void NextGameBanner()
        {
            if (GameBanners.Count < 2) return;
            _currentGameBannerIndex = (_currentGameBannerIndex + 1) % GameBanners.Count;
            OnPropertyChanged(nameof(CurrentGameBanner));
            OnPropertyChanged(nameof(GameBannerPosition));
        }

        private void PreviousGameBanner()
        {
            if (GameBanners.Count < 2) return;
            _currentGameBannerIndex = (_currentGameBannerIndex - 1 + GameBanners.Count) % GameBanners.Count;
            OnPropertyChanged(nameof(CurrentGameBanner));
            OnPropertyChanged(nameof(GameBannerPosition));
        }

        private void SelectGameNewsCategory(string? category)
        {
            if (category is "活动" or "公告" or "资讯")
                SelectedNewsCategory = category;
        }

        private void ExitProgram()
        {
            var result = DialogHelper.ShowYesNo(languages.ExitConfirmMessage, languages.TipsStr);
            if (result) Environment.Exit(0);
        }
        private void MainMinimized() => _main.WindowState = WindowState.Minimized;
        private void ToggleWindowState() => _main.WindowState = _main.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        private void OpenImagesDirectory() => _navigationService.NavigateScreenshots();
        private void OpenAbout()
        {
            var result = DialogHelper.ShowYesNo(languages.AboutStr + "\n\nOpen GitHub?", languages.AboutTitle);
            if (result) FileHelper.OpenUrl("https://github.com/win-syswow64/GenshinImpact_Luncher_plus");
        }
        private async Task RefreshGameNewsAsync()
        {
            _newsCts?.Cancel();
            var refresh = new System.Threading.CancellationTokenSource();
            _newsCts = refresh;
            var token = refresh.Token;
            var gameBiz = _session.Data.ActiveGameBiz;
            IsNewsLoading = true;
            NewsErrorText = string.Empty;
            _bannerTimer.Stop();
            GameBanners = new List<GameBannerItem>();
            ActivityNews = new List<GameNewsItem>();
            AnnouncementNews = new List<GameNewsItem>();
            InformationNews = new List<GameNewsItem>();
            _currentGameBannerIndex = 0;
            OnPropertyChanged(nameof(CurrentGameBanner));
            OnPropertyChanged(nameof(GameBannerPosition));
            OnPropertyChanged(nameof(DisplayedGameNews));
            OnPropertyChanged(nameof(HasDisplayedGameNews));
            OnPropertyChanged(nameof(ShowNewsStatus));
            try
            {
                var content = await HoYoPlayApiService.GetGameLauncherContentAsync(gameBiz, token);
                token.ThrowIfCancellationRequested();
                if (!string.Equals(gameBiz, _session.Data.ActiveGameBiz, StringComparison.OrdinalIgnoreCase))
                    return;

                GameBanners = content.Banners;
                _currentGameBannerIndex = 0;
                OnPropertyChanged(nameof(CurrentGameBanner));
                OnPropertyChanged(nameof(GameBannerPosition));
                if (GameBanners.Count > 1) _bannerTimer.Start(); else _bannerTimer.Stop();

                ActivityNews = content.News.Where(x => x.Category == "活动").Take(3).ToList();
                AnnouncementNews = content.News.Where(x => x.Category == "公告").Take(3).ToList();
                InformationNews = content.News.Where(x => x.Category == "资讯").Take(3).ToList();
                OnPropertyChanged(nameof(DisplayedGameNews));
                OnPropertyChanged(nameof(HasDisplayedGameNews));
                OnPropertyChanged(nameof(ShowNewsStatus));
                OnPropertyChanged(nameof(NewsStatusText));
                if (content.News.Count == 0)
                    NewsErrorText = "未获取到资讯，点击重试";
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                Logger.Debug($"News refresh canceled: {gameBiz}", "HoYoPlay");
            }
            catch (Exception ex)
            {
                NewsErrorText = "资讯加载失败，点击重试";
                Logger.Warn($"News refresh failed: {ex.Message}", "HoYoPlay");
            }
            finally
            {
                if (ReferenceEquals(_newsCts, refresh))
                    IsNewsLoading = false;
            }
        }
    }
}




