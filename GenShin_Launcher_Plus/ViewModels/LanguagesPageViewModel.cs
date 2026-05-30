using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenShin_Launcher_Plus.Models;

namespace GenShin_Launcher_Plus.ViewModels
{
    public class LanguagesPageViewModel : ObservableObject
    {
        public LanguagesPageViewModel()
        {
            SaveLangSetCommand = new RelayCommand(SaveAndRestart);
        }

        public LanguageModel languages => App.Current.Language;

        public int LangIndex => App.Current.DataModel.ReadLang switch
        {
            "Lang_CN" => 0,
            "Lang_TW" => 1,
            "Lang_JP" => 2,
            "Lang_EN" => 3,
            _ => 0,
        };

        public List<LanguageListModel> langlist => App.Current.LangList;

        private string? _switchLang;
        public string? SwitchLang { get => _switchLang; set => SetProperty(ref _switchLang, value); }

        public ICommand SaveLangSetCommand { get; }

        private void SaveAndRestart()
        {
            if (!string.IsNullOrEmpty(SwitchLang))
            {
                App.Current.DataModel.ReadLang = SwitchLang;
            }
            var mainWindow = new MainWindow();
            mainWindow.Show();
            Application.Current.MainWindow.Close();
            Application.Current.MainWindow = mainWindow;
        }
    }
}
