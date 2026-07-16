using System.Collections.Generic;
using GenShin_Launcher_Plus.Models;
using GenShin_Launcher_Plus.Service;

namespace GenShin_Launcher_Plus.Core
{
    /// <summary>
    /// Mutable state for one launcher process. Services receive this session
    /// instead of reaching through WPF's static Application instance.
    /// </summary>
    public interface ILauncherSession
    {
        DataModel Data { get; set; }
        LanguageModel? Language { get; set; }
        List<LanguageListModel>? Languages { get; set; }
        NoticeOverAllBase? AccountOverlay { get; set; }
        NoticeModel? Notice { get; set; }
        UpdateModel? Update { get; set; }
        PkgUpdataModel? PackageUpdate { get; set; }
        MainWindow? MainWindow { get; set; }
        bool IsLoadingBackground { get; set; }
    }

    public sealed class LauncherSession : ILauncherSession
    {
        public LauncherSession(DataModel data) => Data = data;

        public DataModel Data { get; set; }
        public LanguageModel? Language { get; set; }
        public List<LanguageListModel>? Languages { get; set; }
        public NoticeOverAllBase? AccountOverlay { get; set; }
        public NoticeModel? Notice { get; set; }
        public UpdateModel? Update { get; set; }
        public PkgUpdataModel? PackageUpdate { get; set; }
        public MainWindow? MainWindow { get; set; }
        public bool IsLoadingBackground { get; set; }
    }
}
