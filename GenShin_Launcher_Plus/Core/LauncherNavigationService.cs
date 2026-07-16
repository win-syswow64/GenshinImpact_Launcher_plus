using CommunityToolkit.Mvvm.ComponentModel;
using GenShin_Launcher_Plus.Views;

namespace GenShin_Launcher_Plus.Core
{
    /// <summary>
    /// Owns launcher route state and page construction for the main window.
    /// Keeping this outside the window view-model prevents feature commands
    /// from coupling themselves to individual WPF page constructors.
    /// </summary>
    public interface ILauncherNavigationService : System.ComponentModel.INotifyPropertyChanged
    {
        object? CurrentPage { get; }
        void NavigateHome();
        void NavigateGameSettings();
        void NavigateProgramSettings();
        void NavigateUsers();
        void NavigateScreenshots();
    }

    public sealed class LauncherNavigationService : ObservableObject, ILauncherNavigationService
    {
        private object? _currentPage;

        public object? CurrentPage
        {
            get => _currentPage;
            private set => SetProperty(ref _currentPage, value);
        }

        public void NavigateHome() => CurrentPage = new HomePage();
        public void NavigateGameSettings() => CurrentPage = new SettingPage();
        public void NavigateProgramSettings() => CurrentPage = new SettingPage(3);
        public void NavigateUsers() => CurrentPage = new UsersPage();
        public void NavigateScreenshots() => CurrentPage = new ScreenshotsPage();
    }
}
