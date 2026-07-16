using GenShin_Launcher_Plus.Core;
using GenShin_Launcher_Plus.Models;
using GenShin_Launcher_Plus.Service;
using GenShin_Launcher_Plus.Service.IService;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using GenShin_Launcher_Plus.Helper;

namespace GenShin_Launcher_Plus
{
    public partial class App : Application
    {
        private readonly SingleInstanceChecker singleInstanceChecker = new("GenshinLauncherPlus");

        public App()
        {
            Session = new LauncherSession(new DataModel());
            Services = AppServices.CreateDefault(Session);
            LoadProgramCore = Services.GetRequiredService<LoadProgramCore>();
            InitializeComponent();
            ApplyAccentColor();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            RunPathCheck();
            singleInstanceChecker.Ensure(this, BringWindowToFront<MainWindow>);
            base.OnStartup(e);
        }

        private static void RunPathCheck()
        {
            foreach (var profile in Models.GameProfiles.All)
            {
                bool inGameDir = (File.Exists(profile.CnExeName) && Directory.Exists(profile.CnDataFolder))
                              || (File.Exists(profile.GlobalExeName) && Directory.Exists(profile.GlobalDataFolder));
                if (inGameDir)
                {
                    Logger.Error($"Launcher placed inside game directory of [{profile.DisplayName}] - aborting", "App");
                    DialogHelper.ShowInfo("Error: This program cannot be running in this path!");
                    if (Directory.Exists("UserData")) Directory.Delete("UserData", true);
                    if (Directory.Exists("Config")) Directory.Delete("Config", true);
                    Environment.Exit(0);
                    return;
                }
            }
        }

        public static void BringWindowToFront<TWindow>() where TWindow : Window, new()
        {
            TWindow? window = Current.Windows.OfType<TWindow>().FirstOrDefault();
            window ??= new TWindow();
            if (window.WindowState == WindowState.Minimized || window.Visibility != Visibility.Visible)
            {
                window.Show();
                window.WindowState = WindowState.Normal;
            }
            window.Activate();
            window.Topmost = true;
            window.Topmost = false;
            window.Focus();
        }

        public new static App Current => (App)Application.Current;

        public LoadProgramCore LoadProgramCore { get; set; }
        public ILauncherSession Session { get; }
        public AppServices Services { get; }
        public DataModel DataModel { get => Session.Data; set => Session.Data = value; }
        public List<LanguageListModel> LangList { get => Session.Languages!; set => Session.Languages = value; }
        public NoticeOverAllBase NoticeOverAllBase { get => Session.AccountOverlay!; set => Session.AccountOverlay = value; }
        public LanguageModel Language { get => Session.Language!; set => Session.Language = value; }
        public UpdateModel? UpdateObject { get => Session.Update; set => Session.Update = value; }
        public PkgUpdataModel? PkgUpdataModel { get => Session.PackageUpdate; set => Session.PackageUpdate = value; }
        public BackgroundModel? BackgroundModel { get; set; }
        public NoticeModel? NoticeObject { get => Session.Notice; set => Session.Notice = value; }
        public MainWindow ThisMainWindow { get => Session.MainWindow!; set => Session.MainWindow = value; }
        public bool IsLoadUpdated { get; set; }
        public bool IsLoadingBackground { get => Session.IsLoadingBackground; set => Session.IsLoadingBackground = value; }

        private void ApplyAccentColor()
        {
            try
            {
                var hex = DataModel.AccentColor;
                if (string.IsNullOrEmpty(hex)) return;
                var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
                var light = System.Windows.Media.Color.FromRgb(
                    (byte)Math.Min(255, c.R + 60),
                    (byte)Math.Min(255, c.G + 60),
                    (byte)Math.Min(255, c.B + 60));
                var dark = System.Windows.Media.Color.FromRgb(
                    (byte)(c.R * 0.8),
                    (byte)(c.G * 0.8),
                    (byte)(c.B * 0.8));

                Resources["AccentColor"] = c;
                Resources["AccentLightColor"] = light;
                Resources["AccentDarkColor"] = dark;
                Resources["AccentBrush"] = new System.Windows.Media.SolidColorBrush(c);
                Resources["AccentLightBrush"] = new System.Windows.Media.SolidColorBrush(light);
                Resources["AccentDarkBrush"] = new System.Windows.Media.SolidColorBrush(dark);
            }
            catch { /* use defaults */ }
        }

    }
}
