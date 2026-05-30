using System.Windows.Controls;
using GenShin_Launcher_Plus.ViewModels;

namespace GenShin_Launcher_Plus.Views
{
    public partial class UsersPage : UserControl
    {
        public UsersPage()
        {
            InitializeComponent();
            DataContext = new UsersPageViewModel();
        }
    }
}
