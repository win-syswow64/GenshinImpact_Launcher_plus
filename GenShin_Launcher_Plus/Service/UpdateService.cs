using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using GenShin_Launcher_Plus.Helper;
using GenShin_Launcher_Plus.Models;
using GenShin_Launcher_Plus.Service.IService;
using GenShin_Launcher_Plus.ViewModels;
using Newtonsoft.Json;

namespace GenShin_Launcher_Plus.Service
{
    public class UpdateService : IUpdateService
    {
        public async void UpdateRun(UpdatePageViewModel vm)
        {
            if (!vm.ButtonIsEnabled)
            {
                DialogHelper.ShowInfo(App.Current.Language.RepWarnStr, App.Current.Language.TipsStr);
                return;
            }

            vm.ButtonIsEnabled = false;
            vm.ViewControlVisibility = Visibility.Visible;
            string updateFile = vm.UseGlobalUrlCheck
                ? App.Current.UpdateObject.GlobalDownloadUrl
                : App.Current.UpdateObject.DownloadUrl;

            if (await vm.DFC.HttpFileExistAsync(updateFile))
            {
                try
                {
                    await vm.DFC.DownloadHttpFileAsync(updateFile, "UpdateTemp.zip");
                    var result = DialogHelper.ShowYesNo(
                        App.Current.Language.DownloadComStr,
                        App.Current.Language.TipsStr);

                    if (result)
                    {
                        if (FileHelper.UnZip("UpdateTemp.zip"))
                            File.Delete("UpdateTemp.zip");
                        else
                            File.Move("UpdateTemp.zip", "UpdateTemp.upd");

                        Process.Start("Update.exe");
                        Environment.Exit(0);
                    }
                    else
                    {
                        vm.ButtonIsEnabled = true;
                        vm.ViewControlVisibility = Visibility.Collapsed;
                    }
                }
                catch (Exception ex)
                {
                    DialogHelper.ShowError(ex.Message, App.Current.Language.Error);
                    vm.ViewControlVisibility = Visibility.Collapsed;
                    vm.ButtonIsEnabled = true;
                }
            }
            else
            {
                DialogHelper.ShowError(App.Current.Language.DownFailedStr, App.Current.Language.Error);
                vm.ViewControlVisibility = Visibility.Collapsed;
                vm.ButtonIsEnabled = true;
            }
        }

        public async void CheckUpdate(MainWindow main)
        {
            Logger.Debug("Checking for launcher updates", "Update");
            try
            {
                FileHelper.ExtractEmbededAppResource("StaticRes/Update.dll", "Update.exe");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to extract Update.dll: {ex.Message}", "Update");
                DialogHelper.ShowInfo(ex.Message);
            }

            bool isCn = App.Current.DataModel.ReadLang is "Lang_CN" or null or "";
            string json = await HtmlHelper.GetInfoFromHtmlAsync(isCn ? "UpdateCN" : "UpdateGlobal");
            App.Current.UpdateObject = JsonConvert.DeserializeObject<UpdateModel>(json) ?? new();

            App.Current.PkgUpdataModel ??= new PkgUpdataModel();
            App.Current.PkgUpdataModel.PkgVersion = await HtmlHelper.GetPkgVersionAsync();

            string newVer = App.Current.UpdateObject.Version;
            bool requisite = App.Current.UpdateObject.RequisiteUpdate;
            string currentVer = Application.ResourceAssembly.GetName().Version.ToString();

            if (currentVer != newVer && !string.IsNullOrEmpty(newVer) && !App.Current.IsLoadUpdated)
            {
                Logger.Info($"New version available: {currentVer} -> {newVer} (req={requisite})", "Update");
                if (!App.Current.DataModel.IsCloseUpdate || requisite)
                {
                    var updatePage = new Views.UpdatePage();
                    Grid.SetColumnSpan(updatePage, 2);
                    Panel.SetZIndex(updatePage, 999);
                    main.MainGrid.Children.Add(updatePage);
                    App.Current.IsLoadUpdated = true;
                }
            }
        }
    }
}
