using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenShin_Launcher_Plus.Helper;
using GenShin_Launcher_Plus.Models;
using GenShin_Launcher_Plus.Service;
using GenShin_Launcher_Plus.Service.IService;

namespace GenShin_Launcher_Plus.ViewModels
{
    public class UpdatePageViewModel : ObservableObject
    {
        private readonly IUpdateService _updateService;

        public UpdatePageViewModel()
        {
            DFC = new DownloadHelper();
            _updateService = new UpdateService();
            UpdateRunCommand = new RelayCommand(RunUpdate);
            ViewControlVisibility = Visibility.Collapsed;
        }

        public DownloadHelper DFC { get; }
        public LanguageModel languages => App.Current.Language;
        public string Notify => App.Current.UpdateObject?.Content ?? string.Empty;
        public string Title => App.Current.UpdateObject?.Title ?? string.Empty;

        private bool _buttonIsEnabled = true;
        public bool ButtonIsEnabled { get => _buttonIsEnabled; set => SetProperty(ref _buttonIsEnabled, value); }

        private Visibility _viewControlVisibility;
        public Visibility ViewControlVisibility { get => _viewControlVisibility; set => SetProperty(ref _viewControlVisibility, value); }

        private bool _useGlobalUrlCheck;
        public bool UseGlobalUrlCheck { get => _useGlobalUrlCheck; set => SetProperty(ref _useGlobalUrlCheck, value); }

        public ICommand UpdateRunCommand { get; }

        private void RunUpdate()
        {
            _updateService.UpdateRun(this);
        }
    }
}
