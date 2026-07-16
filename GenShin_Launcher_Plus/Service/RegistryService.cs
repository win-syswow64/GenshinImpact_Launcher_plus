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
        private const string StarRailUidRegistryValue = "App_LastUserID_h2841727341";
        private const string GenshinLastUidRegistryValue = "__LastUid___h2153286551";
        private const string Honkai3LastUidRegistryValue = "GENERAL_DATA_V2_LastLoginUserId_h47158221";

        public string? GetFromRegistry(string name, string port, bool isSaveGameConfig)
        {
            var profile = App.Current.DataModel.ActiveGame;
            if (profile == null) return null;
            var biz = App.Current.DataModel.ActiveBiz;
            RegistryModel userRegistry = new();
            userRegistry.Name = name;
            userRegistry.Port = port;
            userRegistry.GameBiz = biz.Value;
            userRegistry.SavedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            try
            {
                if (port == "CN")
                {
                    object? cnsdk = Registry.GetValue(profile.CnRegistryKey, profile.CnSdkRegistryValue, string.Empty);
                    var sdkBytes = ToRegistryBytes(cnsdk);
                    if (sdkBytes.Length == 0)
                        Logger.Warn($"Empty CN SDK registry value: {profile.CnRegistryKey}\\{profile.CnSdkRegistryValue}", "Registry");
                    userRegistry.MIHOYOSDK_ADL_PROD = Encoding.UTF8.GetString(sdkBytes);
                    userRegistry.Uid = ReadUid(profile.CnRegistryKey, biz);
                    Logger.Debug($"Read CN registry for [{profile.DisplayName}]: {name}", "Registry");
                    if (isSaveGameConfig)
                    {
                        object? data = Registry.GetValue(profile.CnRegistryKey, profile.DataRegistryValue, string.Empty);
                        userRegistry.GENERAL_DATA = Encoding.UTF8.GetString(ToRegistryBytes(data));
                    }
                }
                else if (port == "Global")
                {
                    object? globalsdk = Registry.GetValue(profile.GlobalRegistryKey, profile.GlobalSdkRegistryValue, string.Empty);
                    var sdkBytes = ToRegistryBytes(globalsdk);
                    if (sdkBytes.Length == 0)
                        Logger.Warn($"Empty Global SDK registry value: {profile.GlobalRegistryKey}\\{profile.GlobalSdkRegistryValue}", "Registry");
                    userRegistry.MIHOYOSDK_ADL_PROD = Encoding.UTF8.GetString(sdkBytes);
                    userRegistry.Uid = ReadUid(profile.GlobalRegistryKey, biz);
                    Logger.Debug($"Read Global registry for [{profile.DisplayName}]: {name}", "Registry");
                    if (isSaveGameConfig)
                    {
                        object? data = Registry.GetValue(profile.GlobalRegistryKey, profile.DataRegistryValue, string.Empty);
                        userRegistry.GENERAL_DATA = Encoding.UTF8.GetString(ToRegistryBytes(data));
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to read registry: {ex.Message}", "Registry");
                DialogHelper.ShowInfo(App.Current.Language.SaveAccountErr);
            }
            if (string.IsNullOrEmpty(userRegistry.MIHOYOSDK_ADL_PROD))
                return null;
            return JsonConvert.SerializeObject(userRegistry);
        }

        public void SetToRegistry(string name)
        {
            var activeProfile = App.Current.DataModel.ActiveGame;
            if (activeProfile == null) return;
            var profile = activeProfile;
            if (profile == null) return;
            string file = Path.Combine(Directory.GetCurrentDirectory(), "UserData", name);
            string json = File.ReadAllText(file);
            RegistryModel userRegistry = JsonConvert.DeserializeObject<RegistryModel>(json);
            if (string.IsNullOrWhiteSpace(userRegistry.GameBiz))
                throw new InvalidOperationException("Account data has no game/server metadata.");
            var targetBiz = new GameBiz(userRegistry.GameBiz);
            if (!string.Equals(targetBiz.Value, App.Current.DataModel.ActiveGameBiz, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Account belongs to {targetBiz.Value}, current game is {App.Current.DataModel.ActiveGameBiz}.");
            profile = GameProfiles.FindById(targetBiz.Game) ?? activeProfile;
            if (userRegistry.MIHOYOSDK_ADL_PROD != null &&
                userRegistry.MIHOYOSDK_ADL_PROD != "null" &&
                userRegistry.MIHOYOSDK_ADL_PROD != string.Empty)
            {
                if (userRegistry.Port == "CN")
                {
                    Registry.SetValue(profile.CnRegistryKey, profile.CnSdkRegistryValue, Encoding.UTF8.GetBytes(userRegistry.MIHOYOSDK_ADL_PROD));
                    if (userRegistry.GENERAL_DATA != null && userRegistry.GENERAL_DATA != "null" && userRegistry.GENERAL_DATA != string.Empty)
                        Registry.SetValue(profile.CnRegistryKey, profile.DataRegistryValue, Encoding.UTF8.GetBytes(userRegistry.GENERAL_DATA));
                    WriteUid(profile.CnRegistryKey, targetBiz, userRegistry.Uid);
                    Logger.Info($"Set CN registry for [{profile.DisplayName}]: {name}", "Registry");
                }
                else if (userRegistry.Port == "Global")
                {
                    Registry.SetValue(profile.GlobalRegistryKey, profile.GlobalSdkRegistryValue, Encoding.UTF8.GetBytes(userRegistry.MIHOYOSDK_ADL_PROD));
                    if (userRegistry.GENERAL_DATA != null && userRegistry.GENERAL_DATA != "null" && userRegistry.GENERAL_DATA != string.Empty)
                        Registry.SetValue(profile.GlobalRegistryKey, profile.DataRegistryValue, Encoding.UTF8.GetBytes(userRegistry.GENERAL_DATA));
                    WriteUid(profile.GlobalRegistryKey, targetBiz, userRegistry.Uid);
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

        private static byte[] ToRegistryBytes(object? value)
        {
            return value switch
            {
                byte[] bytes => bytes,
                string text when !string.IsNullOrEmpty(text) => Encoding.UTF8.GetBytes(text),
                _ => Array.Empty<byte>(),
            };
        }

        private static string ReadUid(string registryKey, GameBiz biz)
        {
            try
            {
                if (biz.Game == "genshin")
                {
                    if (Registry.GetValue(registryKey, GenshinLastUidRegistryValue, null) is byte[] bytes)
                    {
                        var text = Encoding.UTF8.GetString(bytes).Trim('\0', ' ', '\r', '\n', '\t');
                        return text;
                    }
                }
                if (biz.Game == "starrail")
                {
                    return Convert.ToString(Registry.GetValue(registryKey, StarRailUidRegistryValue, "")) ?? "";
                }
                if (biz.Game == "honkai3")
                {
                    return Convert.ToString(Registry.GetValue(registryKey, Honkai3LastUidRegistryValue, "")) ?? "";
                }
            }
            catch { }
            return "";
        }

        private static void WriteUid(string registryKey, GameBiz biz, string? uid)
        {
            if (string.IsNullOrWhiteSpace(uid))
                return;
            try
            {
                if (biz.Game == "genshin")
                {
                    Registry.SetValue(registryKey, GenshinLastUidRegistryValue, Encoding.UTF8.GetBytes($"{uid}\0"), RegistryValueKind.Binary);
                }
                else if ((biz.Game == "starrail" || biz.Game == "honkai3") && int.TryParse(uid, out var uidValue))
                {
                    Registry.SetValue(registryKey, biz.Game == "starrail" ? StarRailUidRegistryValue : Honkai3LastUidRegistryValue, uidValue, RegistryValueKind.DWord);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Write uid failed: {ex.Message}", "Registry");
            }
        }
    }
}
