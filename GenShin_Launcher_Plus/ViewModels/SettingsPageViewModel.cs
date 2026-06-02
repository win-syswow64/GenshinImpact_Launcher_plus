using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenShin_Launcher_Plus.Helper;
using GenShin_Launcher_Plus.Models;
using GenShin_Launcher_Plus.Service;
using System.Collections.ObjectModel;
using System.Linq;
using GenShin_Launcher_Plus.Services;
using GenShin_Launcher_Plus.Service.IService;
using Microsoft.Win32;

namespace GenShin_Launcher_Plus.ViewModels
{
    public class SettingsPageViewModel : ObservableObject
    {
        private IUserDataService? _userDataService;
        private IRegistryService? _registryService;
        private readonly ISettingService _settingService;

        public SettingsPageViewModel(int mode = 0)
        {
            _settingService = new SettingService(this);
            _isGameMode = mode == 0;
            _flipViewSelectedIndex = mode == 0 ? 0 : 2;
            _navScreenshots = App.Current.DataModel.NavScreenshots;
            _navQQGroup = App.Current.DataModel.NavQQGroup;
            _navAbout = App.Current.DataModel.NavAbout;

            DeleteUserCommand = new RelayCommand(DeleteUser);
            SaveSettingsCommand = new RelayCommand(SaveSettings);
            SaveLangCommand = new RelayCommand(SaveAndRestart);
            ThisPageRemoveCommand = new RelayCommand(ThisPageRemove);
            ChooseGamePathCommand = new RelayCommand(ChooseGamePath);
            AutoSearchGameCommand = new RelayCommand(AutoSearchGame);
            SwitchAccountCommand = new RelayCommand(() => FlipViewSelectedIndex = 1);
            SwitchGameSettingsCommand = new RelayCommand(() => FlipViewSelectedIndex = 0);
            SwitchThemeSettingsCommand = new RelayCommand(() => FlipViewSelectedIndex = 2);
            SwitchLanguageSettingsCommand = new RelayCommand(() => FlipViewSelectedIndex = 3);
            SwitchFunctionSettingsCommand = new RelayCommand(() => FlipViewSelectedIndex = 4);
            SwitchProgramSettingsTabCommand = new RelayCommand(() => FlipViewSelectedIndex = 5);
            CheckUpdateCommand = new RelayCommand(CheckUpdate);
            SaveBackgroundCommand = new RelayCommand(SaveBackground);
            SaveDisplaySizeCommand = new RelayCommand(SaveDisplaySize);
            RemoveDisplaySizeCommand = new RelayCommand(RemoveDisplaySize);
            SetMainBackgroundCommand = new RelayCommand(SetMainBackground);
            OpenApplicationFolderCommand = new RelayCommand(() => FileHelper.OpenUrl(Environment.CurrentDirectory));
            RecoverDefaultSizeCommand = new RelayCommand(RecoverDefaultSize);
            IsDailyBackgroundCommand = new RelayCommand(IsDailyBackground);
            SetAccentColorCommand = new RelayCommand<string>(SetAccentColor);

            InstallAudioPackCommand = new RelayCommand<string>(InstallAudioPack);
            _accentColorIndex = FindAccentColorIndex(App.Current.DataModel.AccentColor);
            _userLists = UserDataService.ReadUserList();
            RefreshAudioPacks();
            _gamePortLists = SettingService.CreateGamePortList();
            _displaySizeLists = SettingService.CreateDisplaySizeList();
            _gameWindowModeList = SettingService.CreateGameWindowModeList();
        }

        private readonly bool _isGameMode;
        public bool IsGameMode => _isGameMode;
        public bool IsProgramMode => !_isGameMode;

        private bool _isPageEnabled = true;
        public bool IsPageEnabled { get => _isPageEnabled; set => SetProperty(ref _isPageEnabled, value); }

        public Visibility GameSettingsVisible => _isGameMode ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ProgramSettingsVisible => _isGameMode ? Visibility.Collapsed : Visibility.Visible;

        // Nav feature toggles
        private bool _navScreenshots;
        public bool NavScreenshots
        {
            get => _navScreenshots;
            set { _navScreenshots = value; App.Current.DataModel.NavScreenshots = value; OnPropertyChanged(); }
        }
        private bool _navQQGroup;
        public bool NavQQGroup
        {
            get => _navQQGroup;
            set { _navQQGroup = value; App.Current.DataModel.NavQQGroup = value; OnPropertyChanged(); }
        }
        private bool _navAbout;
        public bool NavAbout
        {
            get => _navAbout;
            set { _navAbout = value; App.Current.DataModel.NavAbout = value; OnPropertyChanged(); }
        }
        public Visibility NavScreenshotsVisible => _isGameMode ? Visibility.Collapsed : Visibility.Visible;

        // Language selection
        public List<LanguageListModel> LangList => App.Current.LangList;
        public int LangIndex
        {
            get
            {
                var code = LanguageService.Instance.CurrentLangCode;
                var list = App.Current.LangList;
                if (list == null) return 0;
                for (int i = 0; i < list.Count; i++)
                    if (list[i].LangFileName == code) return i;
                return 0;
            }
        }
        private string? _switchLang;
        public string? SwitchLang { get => _switchLang; set => SetProperty(ref _switchLang, value); }
        public ICommand SaveLangCommand { get; }

        private void SaveAndRestart()
        {
            if (!string.IsNullOrEmpty(SwitchLang))
                LanguageService.Instance.SwitchLanguage(SwitchLang);
            App.Current.DataModel.SaveDataToFile();
            App.Current.Language.RaiseAllChanged();
            ThisPageRemove();
        }

        public ISettingService SettingService => _settingService;
        public IUserDataService UserDataService => _userDataService ??= new UserDataService();
        public IRegistryService RegistryService => _registryService ??= new RegistryService();
        public LanguageModel languages => App.Current.Language;

        private string _settingTitleColor = "#FF272727";
        public string SettingTitleColor { get => _settingTitleColor; set => SetProperty(ref _settingTitleColor, value); }

        private void FlashSaveIndicator()
        {
            Task.Run(() =>
            {
                SettingTitleColor = "#FF0F7B0F";
                Thread.Sleep(1500);
                SettingTitleColor = "#FF272727";
            });
        }

        public string SettingsTitle => languages.SettingsTitle;

        // Current server display (read-only, managed by GameBiz selector)
        public int IsMihoyo
        {
            get
            {
                var biz = App.Current.DataModel.ActiveBiz;
                return biz.IsBilibili() ? 1 : biz.IsGlobalServer() ? 2 : 0;
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
                return languages.GameClientTypeNullStr;
            }
        }

        private string _width = string.Empty;
        public string Width
        {
            get => _width;
            set { App.Current.DataModel.Width = value; SetProperty(ref _width, value); }
        }

        private string _height = string.Empty;
        public string Height
        {
            get => _height;
            set { App.Current.DataModel.Height = value; SetProperty(ref _height, value); }
        }

        private int _displaySizeIndex = -1;
        public int DisplaySizeIndex { get => _displaySizeIndex; set => SetProperty(ref _displaySizeIndex, value); }

        private string _gamePath = string.Empty;
        public string GamePath
        {
            get => _gamePath;
            set { App.Current.DataModel.GamePath = value; SetProperty(ref _gamePath, value); }
        }

        private string? _switchUser;
        public string? SwitchUser
        {
            get => _switchUser;
            set { if (value != null) { App.Current.DataModel.SwitchUser = value; SetProperty(ref _switchUser, value); } }
        }

        private bool _isPopup;
        public bool IsPopup
        {
            get => _isPopup;
            set { App.Current.DataModel.IsPopup = value; SetProperty(ref _isPopup, value); }
        }

        private ushort _fullSize;
        public ushort FullSize
        {
            get => _fullSize;
            set { App.Current.DataModel.FullSize = value; SetProperty(ref _fullSize, value); }
        }

        private bool _useXunkongWallpaper;
        public bool UseXunkongWallpaper
        {
            get => _useXunkongWallpaper;
            set { App.Current.DataModel.UseXunkongWallpaper = value; SetProperty(ref _useXunkongWallpaper, value); }
        }

        private bool _isRunThenClose;
        public bool IsRunThenClose
        {
            get => _isRunThenClose;
            set { App.Current.DataModel.IsRunThenClose = value; SetProperty(ref _isRunThenClose, value); }
        }

        private string _customBackgroundPath = string.Empty;
        public string CustomBackgroundPath
        {
            get => _customBackgroundPath;
            set => SetProperty(ref _customBackgroundPath, value);
        }

        // === Background Browser ===
        public class BackgroundItem
        {
            public string Id { get; set; }
            public string ThumbnailUrl { get; set; }
            public string DisplayUrl { get; set; }
            public bool IsVideo { get; set; }
            public string Label { get; set; }
            public Visibility VideoIndicatorVisible => IsVideo ? Visibility.Visible : Visibility.Collapsed;
        }

        private ObservableCollection<BackgroundItem> _backgroundItems = new();
        public ObservableCollection<BackgroundItem> BackgroundItems
        {
            get => _backgroundItems;
            set => SetProperty(ref _backgroundItems, value);
        }

        private int _selectedBackgroundIndex = -1;
        public int SelectedBackgroundIndex
        {
            get => _selectedBackgroundIndex;
            set
            {
                if (SetProperty(ref _selectedBackgroundIndex, value) && value >= 0)
                {
                    _ = ApplyBackgroundAsync(value);
                }
            }
        }

        private bool _isLoadingBackgrounds;
        public bool IsLoadingBackgrounds
        {
            get => _isLoadingBackgrounds;
            set => SetProperty(ref _isLoadingBackgrounds, value);
        }

        public ICommand RefreshBackgroundsCommand => new AsyncRelayCommand(LoadBackgroundsAsync);
        public ICommand ChooseCustomBackgroundCommand => new RelayCommand(ChooseCustomBackground);

        private async Task LoadBackgroundsAsync()
        {
            var profile = App.Current.DataModel.ActiveGame;
            if (profile == null) return;
            IsLoadingBackgrounds = true;
            BackgroundService.ClearApiCache(); // force fresh fetch on manual refresh
            try
            {
                var bgs = await BackgroundService.FetchBackgroundsAsync(profile);
                var items = new ObservableCollection<BackgroundItem>();
                for (int i = 0; i < bgs.Count; i++)
                {
                    var bg = bgs[i];
                    string thumb = bg.Background?.Url ?? bg.Icon?.Url ?? "";
                    string label = bg.IsVideo ? "Video" : $"Image {i + 1}";
                    items.Add(new BackgroundItem
                    {
                        Id = bg.Id,
                        ThumbnailUrl = thumb,
                        DisplayUrl = thumb,
                        IsVideo = bg.IsVideo,
                        Label = label,
                    });
                }
                BackgroundItems = items;

                // Highlight the currently selected one
                string selectedId = App.Current.DataModel.GetSelectedBackgroundId(profile.Id);
                if (!string.IsNullOrEmpty(selectedId))
                {
                    for (int i = 0; i < items.Count; i++)
                        if (items[i].Id == selectedId) { SelectedBackgroundIndex = i; break; }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Failed to load backgrounds: " + ex.Message, "Background");
            }
            IsLoadingBackgrounds = false;
        }

        private async Task ApplyBackgroundAsync(int index)
        {
            if (index < 0 || index >= BackgroundItems.Count) return;
            var profile = App.Current.DataModel.ActiveGame;
            if (profile == null) return;
            var item = BackgroundItems[index];

            try
            {
                // Clear any custom background
                App.Current.DataModel.SetCustomBackground(profile.Id, "");
                App.Current.DataModel.SetSelectedBackgroundId(profile.Id, item.Id);

                // Cache the file
                string url = item.IsVideo
                    ? (await BackgroundService.FetchBackgroundsAsync(profile)).FirstOrDefault(b => b.Id == item.Id)?.Video?.Url
                    : item.DisplayUrl;

                if (!string.IsNullOrEmpty(url))
                {
                    string cached = await BackgroundService.CacheBackgroundFileAsync(url, profile.Id);
                    if (!string.IsNullOrEmpty(cached))
                    {
                        if (item.IsVideo)
                            App.Current.ThisMainWindow.SetBackgroundVideo(cached);
                        else
                            App.Current.ThisMainWindow.SetBackgroundImage(cached);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Failed to apply background: " + ex.Message, "Background");
            }
        }

        private void ChooseCustomBackground()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Image & Video Files (*.png;*.jpg;*.webp;*.mp4;*.mkv;*.webm)|*.png;*.jpg;*.webp;*.mp4;*.mkv;*.webm",
                Title = languages.ChooseBgDialogTitle
            };
            if (dialog.ShowDialog() == true)
            {
                var profile = App.Current.DataModel.ActiveGame;
                if (profile == null) return;

                // Clear API background selection
                App.Current.DataModel.SetSelectedBackgroundId(profile.Id, "");
                App.Current.DataModel.SetCustomBackground(profile.Id, dialog.FileName);
                CustomBackgroundPath = dialog.FileName;

                if (BackgroundService.IsVideoFile(dialog.FileName))
                    App.Current.ThisMainWindow.SetBackgroundVideo(dialog.FileName);
                else
                    App.Current.ThisMainWindow.SetBackgroundImage(dialog.FileName);
            }
        }

        private bool _isCloseUpdate;
        public bool IsCloseUpdate
        {
            get => _isCloseUpdate;
            set { App.Current.DataModel.IsCloseUpdate = value; SetProperty(ref _isCloseUpdate, value); }
        }

        private int _flipViewSelectedIndex;
        public int FlipViewSelectedIndex { get => _flipViewSelectedIndex; set => SetProperty(ref _flipViewSelectedIndex, value); }

        private List<UserListModel> _userLists;
        public List<UserListModel> UserLists { get => _userLists; set => SetProperty(ref _userLists, value); }

        private List<GamePortListModel> _gamePortLists;
        public List<GamePortListModel> GamePortLists => _gamePortLists;

        private List<DisplaySizeListModel>? _displaySizeLists;
        public List<DisplaySizeListModel>? DisplaySizeLists
        {
            get => _displaySizeLists ?? new List<DisplaySizeListModel>
            {
                new() { SizeName = languages.NoSavedPresets, IsNull = true }
            };
            set => SetProperty(ref _displaySizeLists, value);
        }

        private List<GameWindowModeListModel> _gameWindowModeList;
        public List<GameWindowModeListModel> GameWindowModeList => _gameWindowModeList;

        public string SwitchSize { set => SettingService.SetDisplaySelectedValue(value, this); }

        // --- Commands ---
        public ICommand DeleteUserCommand { get; }
        public ICommand SaveSettingsCommand { get; }
        public ICommand ThisPageRemoveCommand { get; }
        public ICommand ChooseGamePathCommand { get; }
        public ICommand AutoSearchGameCommand { get; }
        public ICommand SwitchAccountCommand { get; }
        public ICommand SwitchGameSettingsCommand { get; }
        public ICommand SwitchProgramSettingCommand { get; }
        public ICommand SwitchFunctionSettingsCommand { get; }
        public ICommand SwitchThemeSettingsCommand { get; }
        public ICommand SwitchLanguageSettingsCommand { get; }
        public ICommand SwitchProgramSettingsTabCommand { get; }
        public ICommand CheckUpdateCommand { get; }
        public ICommand SaveBackgroundCommand { get; }
        public ICommand SaveDisplaySizeCommand { get; }
        public ICommand RemoveDisplaySizeCommand { get; }
        public ICommand SetMainBackgroundCommand { get; }
        public ICommand OpenApplicationFolderCommand { get; }
        public ICommand RecoverDefaultSizeCommand { get; }
        public ICommand IsDailyBackgroundCommand { get; }

        private void ChooseGamePath()
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = languages.GameDirMsg,
                ShowNewFolderButton = false
            };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                GamePath = dialog.SelectedPath;
        }

        private void AutoSearchGame()
        {
            var biz = App.Current.DataModel.ActiveBiz;
            var game = App.Current.DataModel.ActiveGame;
            if (game == null) return;

            var result = GameSearchService.FindGame(game, biz.Server);
            if (result != null)
            {
                var gameBiz = $"{game.Id}_{result.Server}";
                App.Current.DataModel.ActiveGameBiz = gameBiz;
                GamePath = result.Path;
                App.Current.DataModel.SetGamePath(gameBiz, result.Path);
                App.Current.DataModel.SaveDataToFile();
                App.Current.ThisMainWindow.ViewModel.RefreshGameSelector();
                // Use LanguageService to get the string with a reliable fallback
                string msg = LanguageService.Instance.GetString("GameFoundMsg");
                if (msg == "GameFoundMsg") msg = "\u627E\u5230\u6E38\u620F\u5BA2\u6237\u7AEF\uFF1A{0}";
                DialogHelper.ShowInfo(string.Format(msg, result.Path), languages.TipsStr);
            }
            else
            {
                string msg = LanguageService.Instance.GetString("GameNotFoundMsg");
                if (msg == "GameNotFoundMsg") msg = "\u672A\u627E\u5230\u6E38\u620F\u5BA2\u6237\u7AEF\uFF0C\u8BF7\u624B\u52A8\u9009\u62E9\u5B89\u88C5\u76EE\u5F55\u3002";
                DialogHelper.ShowWarning(msg, languages.Error);
            }
        }

        // === Audio Language Pack Management ===
        private List<AudioPackInfo> _audioPacks = new();
        public List<AudioPackInfo> AudioPacks { get => _audioPacks; set { SetProperty(ref _audioPacks, value); OnPropertyChanged(nameof(AudioPackDisplayText)); } }

        public string AudioPackDisplayText
        {
            get
            {
                if (AudioPacks.Count == 0) return "未检测到游戏目录";
                var installed = AudioPacks.Where(p => p.IsInstalled).Select(p => p.DisplayName);
                return installed.Any() ? string.Join(", ", installed) : "无已安装音频包";
            }
        }

        private string _audioPackStatusText = "";
        public string AudioPackStatusText { get => _audioPackStatusText; set => SetProperty(ref _audioPackStatusText, value); }

        private bool _isAudioDownloading;
        public bool IsAudioDownloading { get => _isAudioDownloading; set { SetProperty(ref _isAudioDownloading, value); OnPropertyChanged(nameof(AudioPackPanelEnabled)); } }
        public bool AudioPackPanelEnabled => !IsAudioDownloading;

        public ICommand InstallAudioPackCommand { get; }

        private GameInstallService? _audioInstallService;
        public GameInstallService AudioInstallService => _audioInstallService ??= new GameInstallService();

        public void RefreshAudioPacks()
        {
            var path = App.Current.DataModel.GamePath;
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                AudioPacks = new List<AudioPackInfo>();
                return;
            }
            AudioPacks = GameInstallService.GetAudioPackStatus(path);
        }

        private async void InstallAudioPack(string? audioField)
        {
            if (string.IsNullOrEmpty(audioField)) return;
            var gameBiz = App.Current.DataModel.ActiveGameBiz;
            var installPath = App.Current.DataModel.GamePath;
            if (string.IsNullOrEmpty(installPath)) { DialogHelper.ShowWarning("请先设置游戏目录", languages.TipsStr); return; }

            IsAudioDownloading = true;
            AudioPackStatusText = $"正在下载 {audioField} 音频包...";
            try
            {
                _audioInstallService = new GameInstallService();
                OnPropertyChanged(nameof(AudioInstallService));
                await _audioInstallService.DownloadAudioPackAsync(gameBiz, installPath, audioField);
                if (_audioInstallService.State == GameInstallState.Finished)
                {
                    AudioPackStatusText = $"{audioField} 音频包安装完成";
                    RefreshAudioPacks();
                }
                else if (_audioInstallService.State == GameInstallState.Error)
                {
                    AudioPackStatusText = $"安装失败: {_audioInstallService.ErrorText}";
                }
            }
            catch (Exception ex)
            {
                AudioPackStatusText = $"安装失败: {ex.Message}";
                Logger.Error($"Audio pack install failed: {ex}", "Settings");
            }
            finally
            {
                IsAudioDownloading = false;
            }
        }

        private void SaveDisplaySize()
        {
            if (FileHelper.IsInt(Width) && FileHelper.IsInt(Height))
            {
                SettingService.SaveDisplaySizeToList(this, Width, Height);
                DialogHelper.ShowInfo(languages.AddResolutionSuccess, languages.TipsStr);
            }
            else DialogHelper.ShowWarning(languages.ResolutionInputError, languages.Error);
        }

        private void RemoveDisplaySize()
        {
            if (DisplaySizeLists != null && DisplaySizeIndex >= 0)
            {
                SettingService.RemoveDisplaySizeToList(this);
                DialogHelper.ShowInfo(languages.RemoveResolutionSuccess, languages.TipsStr);
            }
            else DialogHelper.ShowWarning(languages.NoPresetSelected, languages.Error);
        }

        private void RecoverDefaultSize()
        {
            double w = SystemParameters.PrimaryScreenWidth * 0.5;
            double h = SystemParameters.PrimaryScreenHeight * 0.5;
            App.Current.ThisMainWindow.Width = w;
            App.Current.ThisMainWindow.Height = h;
            App.Current.DataModel.MainWidth = w;
            App.Current.DataModel.MainHeight = h;
            App.Current.DataModel.SaveDataToFile();
        }

        private void CheckUpdate()
        {
            App.Current.IsLoadUpdated = false;
            App.Current.DataModel.IsCloseUpdate = false;
            new UpdateService().CheckUpdate(App.Current.ThisMainWindow);
        }

        private async void SaveBackground()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JPG Files (*.jpg)|*.jpg",
                Title = languages.SaveImageDialogTitle,
                DefaultExt = "jpg"
            };
            if (dialog.ShowDialog() != true) return;
            try
            {
                string directUrl = App.Current.BackgroundModel?.BackgroundUrl ?? string.Empty;
                if (!string.IsNullOrEmpty(directUrl))
                {
                    using var client = new System.Net.Http.HttpClient(new System.Net.Http.HttpClientHandler
                    {
                        AutomaticDecompression = System.Net.DecompressionMethods.All
                    });
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                    var bytes = await client.GetByteArrayAsync(directUrl);
                    File.WriteAllBytes(dialog.FileName, bytes);
                    DialogHelper.ShowInfo(string.Format(languages.SaveImageSuccess, dialog.FileName), languages.TipsStr);
                    return;
                }
                if (File.Exists("Config/Wallpaper.jpg"))
                {
                    File.Copy("Config/Wallpaper.jpg", dialog.FileName, true);
                    DialogHelper.ShowInfo(string.Format(languages.SaveImageSuccess, dialog.FileName), languages.TipsStr);
                }
            }
            catch
            {
                if (File.Exists("Config/Wallpaper.jpg"))
                {
                    File.Copy("Config/Wallpaper.jpg", dialog.FileName, true);
                    DialogHelper.ShowInfo(string.Format(languages.SaveImageSuccess, dialog.FileName), languages.TipsStr);
                }
            }
        }

        private void SetMainBackground()
        {
            ChooseCustomBackground();
        }

        private void IsDailyBackground()
        {
            if (App.Current.IsLoadingBackground)
            {
                DialogHelper.ShowInfo(languages.WaitBgLoading, languages.TipsStr);
                if (UseXunkongWallpaper) UseXunkongWallpaper = !UseXunkongWallpaper;
                return;
            }
            _ = new MainService(App.Current.ThisMainWindow, App.Current.ThisMainWindow.ViewModel);
        }

        private void DeleteUser()
        {
            if (!string.IsNullOrEmpty(SwitchUser))
            {
                var result = DialogHelper.ShowYesNo($"{languages.WarningDAW}[{SwitchUser}] ? !", languages.Warning);
                if (result)
                {
                    File.Delete(Path.Combine("UserData", SwitchUser));
                    UserLists = UserDataService.ReadUserList();
                    App.Current.NoticeOverAllBase.UserLists = UserLists;
                }
            }
            else DialogHelper.ShowWarning(languages.ErrorSA, languages.Error);
        }

        private void SaveSettings()
        {
            var biz = App.Current.DataModel.ActiveBiz;
            var game = App.Current.DataModel.ActiveGame;
            if (game == null) return;
            string exe = game.GetExeName(biz);
            string exePath = Path.Combine(GamePath, exe);
            if (string.IsNullOrEmpty(GamePath) || !File.Exists(exePath))
            {
                DialogHelper.ShowWarning(languages.PathErrorMessageStr, languages.Error);
                return;
            }
            App.Current.DataModel.GamePath = GamePath;
            Logger.Info($"Saving game path: {GamePath} for {biz}", "Settings");
            if (!string.IsNullOrEmpty(SwitchUser))
            {
                App.Current.NoticeOverAllBase.SwitchUser = $"{languages.UserNameLab}��{SwitchUser}";
                App.Current.NoticeOverAllBase.IsSwitchUser = Visibility.Visible;
                RegistryService.SetToRegistry(SwitchUser);
            }
            App.Current.DataModel.Height = Height;
            App.Current.DataModel.Width = Width;
            FlashSaveIndicator();
            App.Current.DataModel.SaveDataToFile();
            ThisPageRemove();
        }

        private void ThisPageRemove()
        {
            App.Current.ThisMainWindow.NavigateBack();
        }

        // Accent color presets
        public string[] AccentColors { get; } = new[]
        {
            "#FF69B4", "#4B7BEC", "#A855F7", "#22C55E",
            "#F59E0B", "#EF4444", "#06B6D4", "#F472B6"
        };

        private int _accentColorIndex;
        public int AccentColorIndex { get => _accentColorIndex; set => SetProperty(ref _accentColorIndex, value); }

        public ICommand SetAccentColorCommand { get; }

        private int FindAccentColorIndex(string hex)
        {
            for (int i = 0; i < AccentColors.Length; i++)
                if (string.Equals(AccentColors[i], hex, StringComparison.OrdinalIgnoreCase))
                    return i;
            return 0;
        }

        private void SetAccentColor(string? hex)
        {
            if (string.IsNullOrEmpty(hex)) return;
            App.Current.DataModel.AccentColor = hex;
            AccentColorIndex = FindAccentColorIndex(hex);

            var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            var light = System.Windows.Media.Color.FromRgb(
                (byte)Math.Min(255, c.R + 60),
                (byte)Math.Min(255, c.G + 60),
                (byte)Math.Min(255, c.B + 60));
            var dark = System.Windows.Media.Color.FromRgb(
                (byte)(c.R * 0.8),
                (byte)(c.G * 0.8),
                (byte)(c.B * 0.8));

            Application.Current.Resources["AccentColor"] = c;
            Application.Current.Resources["AccentLightColor"] = light;
            Application.Current.Resources["AccentDarkColor"] = dark;
            Application.Current.Resources["AccentBrush"] = new System.Windows.Media.SolidColorBrush(c);
            Application.Current.Resources["AccentLightBrush"] = new System.Windows.Media.SolidColorBrush(light);
            Application.Current.Resources["AccentDarkBrush"] = new System.Windows.Media.SolidColorBrush(dark);
        }
    }
}
