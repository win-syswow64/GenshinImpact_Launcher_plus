using System.Threading.Tasks;

namespace GenShin_Launcher_Plus.Service.IService
{
    public interface ILaunchService
    {
        Task RunGameAsync();
        void ReadUserList();
    }
}