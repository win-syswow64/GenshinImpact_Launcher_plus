using GenShin_Launcher_Plus.Models;
using GenShin_Launcher_Plus.Services;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;

namespace GenShin_Launcher_Plus.Core
{
    public class LoadProgramCore
    {
        public LoadProgramCore()
        {
            App.Current.NoticeOverAllBase = new();
        }

        public void LoadLanguageCore()
        {
            // Create LanguageModel FIRST so ApplyToModel has a valid target
            App.Current.Language ??= new LanguageModel();

            var langService = LanguageService.Instance;
            langService.Initialize();

            // Populate LangList for the settings UI
            App.Current.LangList = langService.AvailableLanguages.Select(l => new LanguageListModel
            {
                LangID = l.Code,
                LangVersion = "2.0",
                LangName = l.NativeName,
                LangFileName = l.Code
            }).ToList();
        }
    }
}
