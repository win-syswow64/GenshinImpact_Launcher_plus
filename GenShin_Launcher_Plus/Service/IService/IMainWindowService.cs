using System.Threading.Tasks;
using GenShin_Launcher_Plus.ViewModels;

namespace GenShin_Launcher_Plus.Service.IService
{
    public interface IMainWindowService
    {
        void CheckConfig(MainWindow main);
        Task MainBackgroundLoadAsync(MainWindowViewModel vm);
        Task LoadGameBackgroundAsync();
        Task CheckNotice();
    }
}
