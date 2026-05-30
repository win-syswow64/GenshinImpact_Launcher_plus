using System;
using Microsoft.Win32;
using System.Text;
using GenShin_Launcher_Plus.Models;
using Newtonsoft.Json;
using System.IO;
using System.Windows;
using GenShin_Launcher_Plus.Service.IService;
using GenShin_Launcher_Plus.Helper;

namespace GenShin_Launcher_Plus.Service
{
    /// <summary>
    /// 游戏注册表操作，通过 GameProfile 获取注册表路径
    /// </summary>
    public class RegistryService : IRegistryService
    {
        public string? GetFromRegistry(string name, string port, bool isSaveGameConfig)
        {
            var profile = App.Current.DataModel.ActiveGame;
            if (profile == null) return null;
            RegistryModel userRegistry = new();
            userRegistry.Name = name;
            userRegistry.Port = port;
            try
            {
                if (port == "CN")
                {
                    object? cnsdk = Registry.GetValue(profile.CnRegistryKey, profile.CnSdkRegistryValue, string.Empty);
                    userRegistry.MIHOYOSDK_ADL_PROD = Encoding.UTF8.GetString((byte[])cnsdk);
                    Logger.Debug($"Read CN registry for [{profile.DisplayName}]: {name}", "Registry");
                    if (isSaveGameConfig)
                    {
                        object? data = Registry.GetValue(profile.CnRegistryKey, profile.DataRegistryValue, string.Empty);
                        userRegistry.GENERAL_DATA = Encoding.UTF8.GetString((byte[])data);
                    }
                }
                else if (port == "Global")
                {
                    object? globalsdk = Registry.GetValue(profile.GlobalRegistryKey, profile.GlobalSdkRegistryValue, string.Empty);
                    userRegistry.MIHOYOSDK_ADL_PROD = Encoding.UTF8.GetString((byte[])globalsdk);
                    Logger.Debug($"Read Global registry for [{profile.DisplayName}]: {name}", "Registry");
                    if (isSaveGameConfig)
                    {
                        object? data = Registry.GetValue(profile.GlobalRegistryKey, profile.DataRegistryValue, string.Empty);
                        userRegistry.GENERAL_DATA = Encoding.UTF8.GetString((byte[])data);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to read registry: {ex.Message}", "Registry");
                DialogHelper.ShowInfo(App.Current.Language.SaveAccountErr);
            }
            return JsonConvert.SerializeObject(userRegistry);
        }

        public void SetToRegistry(string name)
        {
            var profile = App.Current.DataModel.ActiveGame;
            if (profile == null) return;
            string file = Path.Combine(Directory.GetCurrentDirectory(), "UserData", name);
            string json = File.ReadAllText(file);
            RegistryModel userRegistry = JsonConvert.DeserializeObject<RegistryModel>(json);
            if (userRegistry.MIHOYOSDK_ADL_PROD != null &&
                userRegistry.MIHOYOSDK_ADL_PROD != "null" &&
                userRegistry.MIHOYOSDK_ADL_PROD != string.Empty)
            {
                if (userRegistry.Port == "CN")
                {
                    Registry.SetValue(profile.CnRegistryKey, profile.CnSdkRegistryValue, Encoding.UTF8.GetBytes(userRegistry.MIHOYOSDK_ADL_PROD));
                    if (userRegistry.GENERAL_DATA != null && userRegistry.GENERAL_DATA != "null" && userRegistry.GENERAL_DATA != string.Empty)
                        Registry.SetValue(profile.CnRegistryKey, profile.DataRegistryValue, Encoding.UTF8.GetBytes(userRegistry.GENERAL_DATA));
                    Logger.Info($"Set CN registry for [{profile.DisplayName}]: {name}", "Registry");
                }
                else if (userRegistry.Port == "Global")
                {
                    Registry.SetValue(profile.GlobalRegistryKey, profile.GlobalSdkRegistryValue, Encoding.UTF8.GetBytes(userRegistry.MIHOYOSDK_ADL_PROD));
                    if (userRegistry.GENERAL_DATA != null && userRegistry.GENERAL_DATA != "null" && userRegistry.GENERAL_DATA != string.Empty)
                        Registry.SetValue(profile.GlobalRegistryKey, profile.DataRegistryValue, Encoding.UTF8.GetBytes(userRegistry.GENERAL_DATA));
                    Logger.Info($"Set Global registry for [{profile.DisplayName}]: {name}", "Registry");
                }
                else
                {
                    Logger.Warn($"Unknown registry port: {userRegistry.Port}", "Registry");
                    DialogHelper.ShowInfo("Error : The file does not support ! !");
                }
            }
            else
            {
                Logger.Warn($"Invalid registry data for: {name}", "Registry");
                DialogHelper.ShowInfo("Error : The file does not support ! !");
            }
        }
    }
}
