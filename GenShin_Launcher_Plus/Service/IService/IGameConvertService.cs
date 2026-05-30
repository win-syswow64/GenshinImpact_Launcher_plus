using GenShin_Launcher_Plus.ViewModels;

namespace GenShin_Launcher_Plus.Service.IService
{
    /// <summary>
    /// Server config management interface.
    /// Replaces the old PKG-based IGameConvertService with Starward-style Config.ini management.
    /// </summary>
    public interface IGameConvertService
    {
        /// <summary>
        /// Apply server config (cps, channel, sub_channel) to the game''s Config.ini
        /// and manage the Bilibili SDK DLL. Called before launching the game.
        /// </summary>
        void SaveGameConfig(SettingsPageViewModel vm);
    }
}