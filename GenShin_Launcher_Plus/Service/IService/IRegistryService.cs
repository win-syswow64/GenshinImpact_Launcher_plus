namespace GenShin_Launcher_Plus.Service.IService
{
    public interface IRegistryService
    {
        string? GetFromRegistry(string name, string port, bool isSaveGameConfig);
        void SetToRegistry(string name);
    }
}
