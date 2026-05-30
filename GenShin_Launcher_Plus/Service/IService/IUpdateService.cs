using GenShin_Launcher_Plus.ViewModels;

namespace GenShin_Launcher_Plus.Service.IService
{
    public interface IUpdateService
    {
        void CheckUpdate(MainWindow main);
        void UpdateRun(UpdatePageViewModel vm);
    }
}
