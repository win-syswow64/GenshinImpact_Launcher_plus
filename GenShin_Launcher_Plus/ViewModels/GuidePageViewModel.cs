using System.IO;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenShin_Launcher_Plus.Core;
using GenShin_Launcher_Plus.Models;
using GenShin_Launcher_Plus.Service;
using GenShin_Launcher_Plus.Services;
using GenShin_Launcher_Plus.Helper;

namespace GenShin_Launcher_Plus.ViewModels
{
    public class GuidePageViewModel : ObservableObject
    {
        public GuidePageViewModel()
        {
            DirchooseCommand = new RelayCommand(Dirchoose);
            AutoSearchCommand = new RelayCommand(AutoSearch);
        }

        private string _gamePath = string.Empty;
        public string GamePath { get => _gamePath; set => SetProperty(ref _gamePath, value); }

        public LanguageModel languages => App.Current.Language;

        public ICommand DirchooseCommand { get; }
        public ICommand AutoSearchCommand { get; }

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

        private void AutoSearch()
        {
            var biz = App.Current.DataModel.ActiveBiz;
            var game = App.Current.DataModel.ActiveGame;
            if (game == null) return;

            var path = GameSearchService.FindGamePath(game, biz.Server);
            if (path != null)
            {
                GamePath = path;
                App.Current.DataModel.GamePath = path;
                App.Current.DataModel.SaveDataToFile();
                App.Current.DataModel = new DataModel();
                var mainWindow = new MainWindow();
                mainWindow.Show();
                Application.Current.MainWindow.Close();
                Application.Current.MainWindow = mainWindow;
            }
            else
            {
                string msg = LanguageService.Instance.GetString("GameNotFoundMsg");
                if (msg == "GameNotFoundMsg") msg = "\u672A\u627E\u5230\u6E38\u620F\u5BA2\u6237\u7AEF\uFF0C\u8BF7\u624B\u52A8\u9009\u62E9\u5B89\u88C5\u76EE\u5F55\u3002";
                DialogHelper.ShowWarning(msg, languages.Error);
            }
        }
    }
}