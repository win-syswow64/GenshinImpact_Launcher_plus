using GenShin_Launcher_Plus.Models;
using GenShin_Launcher_Plus.Services;
using GenShin_Launcher_Plus.Service.IService;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;

namespace GenShin_Launcher_Plus.Core
{
    public class LoadProgramCore
    {
        private readonly ILauncherSession _session;

        public LoadProgramCore(ILauncherSession session, IRegistryService registryService)
        {
            _session = session;
            _session.AccountOverlay = new(session, registryService);
        }

        public void LoadLanguageCore()
        {
            // Create LanguageModel FIRST so ApplyToModel has a valid target
            _session.Language ??= new LanguageModel();

            var langService = LanguageService.Instance;
            langService.Initialize();

            // Populate LangList for the settings UI
            _session.Languages = langService.AvailableLanguages.Select(l => new LanguageListModel
            {
                LangID = l.Code,
                LangVersion = "2.0",
                LangName = l.NativeName,
                LangFileName = l.Code
            }).ToList();
        }
    }
}
