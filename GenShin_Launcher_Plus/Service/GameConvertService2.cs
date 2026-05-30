using GenShin_Launcher_Plus.Core;
using GenShin_Launcher_Plus.Helper;
using GenShin_Launcher_Plus.Models;
using System;
using System.IO;
using System.Windows;

namespace GenShin_Launcher_Plus.Service
{
    /// <summary>
    /// Manages the game''s Config.ini (cps, channel, sub_channel) and Bilibili SDK DLL.
    /// Modeled after Starward: writes correct settings at launch time, no PKG-based conversion.
    /// </summary>
    internal class GameConfigService
    {
        private readonly GameProfile _profile;
        private readonly GameBiz _biz;
        private readonly string _gamePath;

        public GameConfigService(GameProfile profile, GameBiz biz, string gamePath)
        {
            _profile = profile;
            _biz = biz;
            _gamePath = gamePath;
        }

        /// <summary>
        /// Write the correct cps/channel/sub_channel to the game''s Config.ini
        /// and manage the Bilibili SDK DLL based on the selected server.
        /// Call this before launching the game.
        /// </summary>
        public void ApplyServerConfig()
        {
            string configPath = Path.Combine(_gamePath, "Config.ini");
            if (!File.Exists(configPath)) return;

            string cps;
            int channel;
            int subChannel;

            if (_biz.IsBilibili())
            {
                cps = "bilibili";
                channel = 14;
                subChannel = 0;
            }
            else if (_biz.IsGlobalServer())
            {
                cps = "hoyoverse";
                channel = 1;
                subChannel = 0;
            }
            else // CN
            {
                cps = "mihoyo";
                channel = 1;
                subChannel = 1;
            }

            // Write to Config.ini via IniParser
            var gp = new IniParser(configPath);
            gp.AddSetting("General", "cps", cps);
            gp.AddSetting("General", "channel", Convert.ToString(channel));
            gp.AddSetting("General", "sub_channel", Convert.ToString(subChannel));
            gp.SaveSettings();

            // Manage Bilibili SDK DLL
            ManageBilibiliSdk();

            Logger.Info($"Applied server config: {_biz} -> cps={cps}, channel={channel}, sub_channel={subChannel}", "Config");
        }

        /// <summary>
        /// Ensure the Bilibili SDK DLL exists only for Bilibili server, removed for others.
        /// </summary>
        private void ManageBilibiliSdk()
        {
            if (_profile?.BilibiliSdkPath == null) return;

            string dataFolder = _profile.GetDataFolder(_biz);
            string sdkPath = Path.Combine(_gamePath, dataFolder, _profile.BilibiliSdkPath);

            if (_biz.IsBilibili())
            {
                // Ensure SDK exists
                if (!File.Exists(sdkPath))
                {
                    try
                    {
                        FileHelper.ExtractEmbededAppResource("StaticRes/mihoyosdk.dll", sdkPath);
                        Logger.Info($"Extracted Bilibili SDK to {sdkPath}", "Config");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Failed to extract Bilibili SDK: {ex.Message}", "Config");
                    }
                }
            }
            else
            {
                // Remove SDK if present
                if (File.Exists(sdkPath))
                {
                    try
                    {
                        File.Delete(sdkPath);
                        Logger.Info($"Removed Bilibili SDK from {sdkPath}", "Config");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Failed to remove Bilibili SDK: {ex.Message}", "Config");
                    }
                }
            }
        }

        /// <summary>
        /// Read the current cps value from Config.ini.
        /// </summary>
        public static string ReadCps(string gamePath)
        {
            string configPath = Path.Combine(gamePath, "Config.ini");
            if (!File.Exists(configPath)) return null;
            var gp = new IniParser(configPath);
            return gp.GetSetting("General", "cps", 0);
        }
    }
}