using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GenShin_Launcher_Plus.ViewModels;

namespace GenShin_Launcher_Plus.Views
{
    public partial class SettingPage : UserControl
    {
        private SettingsPageViewModel ViewModel => (SettingsPageViewModel)DataContext;

        public SettingPage(int tabIndex = 0)
        {
            InitializeComponent();
            DataContext = App.Current.Services.CreateSettingsPageViewModel(tabIndex);
            Loaded += SettingPage_Loaded;
        }

        private void SettingPage_Loaded(object sender, RoutedEventArgs e)
        {
            // Auto-load backgrounds when the theme tab is visible
            if (ViewModel.IsProgramMode)
            {
                ViewModel.RefreshBackgroundsCommand.Execute(null);
            }
        }

        public void RefreshForActiveGame()
        {
            ViewModel.RefreshForActiveGame();
        }

        private void AccentColor_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string hex)
            {
                ViewModel.SetAccentColorCommand.Execute(hex);
            }
        }

        private void BackgroundThumb_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is SettingsPageViewModel.BackgroundItem item)
            {
                var items = ViewModel.BackgroundItems;
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i].Id == item.Id)
                    {
                        ViewModel.SelectedBackgroundIndex = i;
                        break;
                    }
                }
            }
        }
    }
}
