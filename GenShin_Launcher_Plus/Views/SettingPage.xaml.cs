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
            DataContext = new SettingsPageViewModel(tabIndex);
        }

        private void AccentColor_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string hex)
            {
                ViewModel.SetAccentColorCommand.Execute(hex);
            }
        }
    }
}