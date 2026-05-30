using System.Windows;
using System.Windows.Controls;
using GenShin_Launcher_Plus.ViewModels;

namespace GenShin_Launcher_Plus.Views
{
    public partial class LanguagesPage : UserControl
    {
        public LanguagesPage()
        {
            InitializeComponent();
            DataContext = new LanguagesPageViewModel();
        }

        private void RemoveThisPage(object sender, RoutedEventArgs e)
        {
            App.Current.ThisMainWindow.ViewModel.NavigateHomeCommand.Execute(null);
        }
    }
}
