using System;
using System.Collections.Generic;
using System.IO;
using GenShin_Launcher_Plus.Models;
using GenShin_Launcher_Plus.Service.IService;
using GenShin_Launcher_Plus.ViewModels;
using Newtonsoft.Json;

namespace GenShin_Launcher_Plus.Service
{
    public class SettingService : ISettingService
    {
        public SettingService(SettingsPageViewModel vm)
        {
            vm.Width = App.Current.DataModel.Width ?? "1600";
            vm.Height = App.Current.DataModel.Height ?? "900";
            vm.GamePath = App.Current.DataModel.GamePath;
            vm.SwitchUser = App.Current.DataModel.SwitchUser;
            vm.IsPopup = App.Current.DataModel.IsPopup;
            vm.FullSize = App.Current.DataModel.FullSize;
            vm.UseXunkongWallpaper = App.Current.DataModel.UseXunkongWallpaper;
            vm.IsRunThenClose = App.Current.DataModel.IsRunThenClose;
            vm.IsCloseUpdate = App.Current.DataModel.IsCloseUpdate;
            vm.NavScreenshots = App.Current.DataModel.NavScreenshots;
            vm.NavQQGroup = App.Current.DataModel.NavQQGroup;
            vm.NavAbout = App.Current.DataModel.NavAbout;
            // Server is now managed via GameBiz selector, not conversion

            // Server is managed via GameBiz selector in MainWindowViewModel
        }

        public List<DisplaySizeListModel> CreateDisplaySizeList()
        {
            if (File.Exists("Config/DisplaySize.json"))
            {
                string json = File.ReadAllText("Config/DisplaySize.json");
                if (json == "[]") return null;
                return JsonConvert.DeserializeObject<List<DisplaySizeListModel>>(json);
            }
            return null;
        }

        public List<GamePortListModel> CreateGamePortList()
        {
            return new List<GamePortListModel>
            {
                new() { GamePort = App.Current.Language.GameClientTypePStr },
                new() { GamePort = App.Current.Language.GameClientTypeBStr },
                new() { GamePort = App.Current.Language.GameClientTypeMStr }
            };
        }

        public List<GameWindowModeListModel> CreateGameWindowModeList()
        {
            return new List<GameWindowModeListModel>
            {
                new() { GameWindowMode = App.Current.Language.WindowMode },
                new() { GameWindowMode = App.Current.Language.Fullscreen }
            };
        }

        public void SetDisplaySelectedValue(string sizeName, SettingsPageViewModel vm)
        {
            if (vm.DisplaySizeLists == null) return;
            foreach (var dsm in vm.DisplaySizeLists)
            {
                if (sizeName == dsm.SizeName)
                {
                    vm.Height = dsm.Height;
                    vm.Width = dsm.Width;
                    return;
                }
            }
        }

        public void SaveDisplaySizeToList(SettingsPageViewModel vm, string width, string height)
        {
            var allList = new List<DisplaySizeListModel>();
            if (vm.DisplaySizeLists != null)
            {
                foreach (var dsm in vm.DisplaySizeLists)
                {
                    if (!dsm.IsNull) allList.Add(dsm);
                }
            }

            foreach (var dsm in allList)
            {
                if ($"{width} x {height}" == dsm.SizeName) return;
            }

            int divisor = GetGcd(Convert.ToInt32(width), Convert.ToInt32(height));
            string ratio = $"{Convert.ToInt32(width) / divisor}:{Convert.ToInt32(height) / divisor}";
            allList.Add(new DisplaySizeListModel
            {
                Width = width,
                Height = height,
                SizeName = $"{width} x {height}  |  {ratio}",
                IsNull = false
            });

            File.WriteAllText("Config/DisplaySize.json", JsonConvert.SerializeObject(allList));
            vm.DisplaySizeLists = allList;
        }

        public void RemoveDisplaySizeToList(SettingsPageViewModel vm)
        {
            if (vm.DisplaySizeLists == null || vm.DisplaySizeIndex < 0) return;
            var allList = new List<DisplaySizeListModel>();
            foreach (var dsm in vm.DisplaySizeLists)
            {
                if (!dsm.IsNull) allList.Add(dsm);
            }
            if (vm.DisplaySizeIndex < allList.Count)
            {
                allList.RemoveAt(vm.DisplaySizeIndex);
            }
            File.WriteAllText("Config/DisplaySize.json", JsonConvert.SerializeObject(allList));
            vm.DisplaySizeLists = allList.Count > 0 ? allList : null;
        }

        public List<DailyImageArray> ReadDailyImageSourceFromJson()
        {
            if (File.Exists("Config/DailyImagePids.json"))
            {
                string json = File.ReadAllText("Config/DailyImagePids.json");
                if (json == "[]") return null;
                return JsonConvert.DeserializeObject<List<DailyImageArray>>(json);
            }
            return null;
        }

        public bool SetDailyImageDataToJson(SettingsPageViewModel vm)
        {
            return false; // Feature disabled
        }

        private static int GetGcd(int a, int b) => b == 0 ? a : GetGcd(b, a % b);
    }
}