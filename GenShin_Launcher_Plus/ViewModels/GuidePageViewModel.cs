using System.IO;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenShin_Launcher_Plus.Core;
using GenShin_Launcher_Plus.Models;
using GenShin_Launcher_Plus.Service;
using GenShin_Launcher_Plus.Service.IService;
using GenShin_Launcher_Plus.Helper;

namespace GenShin_Launcher_Plus.ViewModels
{
    public class GuidePageViewModel : ObservableObject
    {
        public GuidePageViewModel()
        {
            DirchooseCommand = new RelayCommand(Dirchoose);
        }

        private string _gamePath = string.Empty;
        public string GamePath { get => _gamePath; set => SetProperty(ref _gamePath, value); }

        public LanguageModel languages => App.Current.Language;

        public ICommand DirchooseCommand { get; }

        private void Dirchoose()
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = App.Current.Language.GameDirMsg,
                ShowNewFolderButton = false
            };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                GamePath = dialog.SelectedPath;
                var game = App.Current.DataModel.ActiveGame;
                if (game == null) return;
                if (!File.Exists(Path.Combine(GamePath, game.CnExeName)) &&
                    !File.Exists(Path.Combine(GamePath, game.GlobalExeName)))
                {
                    DialogHelper.ShowWarning(languages.PathErrorMessageStr, languages.Error);
                }
                else
                {
                    App.Current.DataModel.GamePath = GamePath;
                    App.Current.DataModel.SaveDataToFile();
                    App.Current.DataModel = new DataModel();
                    var mainWindow = new MainWindow();
                    mainWindow.Show();
                    Application.Current.MainWindow.Close();
                    Application.Current.MainWindow = mainWindow;
                }
            }
        }
    }
}
