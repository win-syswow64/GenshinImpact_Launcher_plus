using System.Threading.Tasks;
using GenShin_Launcher_Plus.ViewModels;
using System.Threading;

namespace GenShin_Launcher_Plus.Service.IService
{
    public interface IMainWindowService
    {
        Task<bool> CheckConfigAsync(MainWindow main);
        Task MainBackgroundLoadAsync(MainWindowViewModel vm);
        Task LoadGameBackgroundAsync(CancellationToken cancellationToken = default);
        Task CheckNotice();
    }
}
