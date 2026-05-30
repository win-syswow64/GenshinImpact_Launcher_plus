using System.Windows;
using System.Windows.Controls;
using GenShin_Launcher_Plus.ViewModels;

namespace GenShin_Launcher_Plus.Views
{
    public partial class UpdatePage : UserControl
    {
        public UpdatePage()
        {
            InitializeComponent();
            DataContext = new UpdatePageViewModel();
        }

        private void SkipButton_Click(object sender, RoutedEventArgs e)
        {
            ((Panel)Parent).Children.Remove(this);
        }
    }
}
