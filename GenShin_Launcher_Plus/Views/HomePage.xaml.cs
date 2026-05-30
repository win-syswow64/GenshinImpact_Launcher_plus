using System.Windows.Controls;
using GenShin_Launcher_Plus.ViewModels;

namespace GenShin_Launcher_Plus.Views
{
    public partial class HomePage : UserControl
    {
        public HomePage()
        {
            InitializeComponent();
            DataContext = new HomePageViewModel();
        }
    }
}
