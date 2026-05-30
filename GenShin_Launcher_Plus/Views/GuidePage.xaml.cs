using System.Windows.Controls;
using GenShin_Launcher_Plus.ViewModels;

namespace GenShin_Launcher_Plus.Views
{
    public partial class GuidePage : UserControl
    {
        public GuidePage()
        {
            InitializeComponent();
            DataContext = new GuidePageViewModel();
        }
    }
}
