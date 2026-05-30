using GenShin_Launcher_Plus.Models;
using GenShin_Launcher_Plus.ViewModels;
using System.Collections.Generic;

namespace GenShin_Launcher_Plus.Service.IService
{
    public interface ISettingService
    {
        void SetDisplaySelectedValue(string sizeName, SettingsPageViewModel vm);
        void SaveDisplaySizeToList(SettingsPageViewModel vm, string width, string height);
        void RemoveDisplaySizeToList(SettingsPageViewModel vm);
        bool SetDailyImageDataToJson(SettingsPageViewModel vm);
        List<DisplaySizeListModel> CreateDisplaySizeList();
        List<GameWindowModeListModel> CreateGameWindowModeList();
        List<GamePortListModel> CreateGamePortList();
        List<DailyImageArray> ReadDailyImageSourceFromJson();
    }
}