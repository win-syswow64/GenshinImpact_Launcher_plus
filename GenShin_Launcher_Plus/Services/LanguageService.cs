using System;
using System.Collections.Generic;
using GenShin_Launcher_Plus.Helper;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Reflection;
using Newtonsoft.Json;
using GenShin_Launcher_Plus.Models;

namespace GenShin_Launcher_Plus.Services
{
    public sealed class LanguageService
    {
        private static readonly Lazy<LanguageService> _instance = new(() => new LanguageService());
        public static LanguageService Instance => _instance.Value;

        private const string LangDir = "Lang";
        private const string FallbackLang = "en";
        private const string EmbeddedResourcePrefix = "GenShin_Launcher_Plus.Lang.";
        private Dictionary<string, string> _fallbackStrings = new();
        private Dictionary<string, string> _currentStrings = new();

        public List<LanguageInfo> AvailableLanguages { get; private set; } = new();
        public string CurrentLangCode { get; private set; } = FallbackLang;

        private LanguageService() { }

        public void Initialize()
        {
            Logger.Info("Language service initializing", "Lang");
            ScanAvailableLanguages();

            var savedLang = App.Current.DataModel.ReadLang;
            Logger.Debug($"Saved lang: {savedLang}", "Lang");

            string langCode;
            if (string.IsNullOrEmpty(savedLang))
            {
                langCode = DetectSystemLanguage();
                Logger.Info($"No saved lang, detected: {langCode}", "Lang");
            }
            else
            {
                langCode = MapLegacyCode(savedLang);
                Logger.Debug($"Mapped legacy: {savedLang} -> {langCode}", "Lang");
            }

            // Verify the detected/saved language is available
            if (!IsLanguageAvailable(langCode))
            {
                Logger.Warn($"Language {langCode} not available, fallback to {FallbackLang}", "Lang");
                langCode = FallbackLang;
            }

            Logger.Info($"Loading language: {langCode}", "Lang");
            LoadLanguage(langCode);
        }

        private bool IsLanguageAvailable(string langCode)
        {
            return AvailableLanguages.Any(l => l.Code == langCode);
        }

        private void ScanAvailableLanguages()
        {
            AvailableLanguages.Clear();
            var fullPath = Path.GetFullPath(LangDir);
            Logger.Debug($"Scanning: {fullPath}", "Lang");

            // Auto-extract bundled languages if Lang folder is missing or empty
            if (!Directory.Exists(LangDir) || Directory.GetFiles(LangDir, "*.json").Length == 0)
            {
                ExtractDefaultLanguages();
            }

            if (!Directory.Exists(LangDir))
            {
                Logger.Warn("Lang directory does not exist after extraction!", "Lang");
                return;
            }

            foreach (var file in Directory.GetFiles(LangDir, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var data = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                    if (data != null && data.ContainsKey("Languages"))
                    {
                        var code = Path.GetFileNameWithoutExtension(file);
                        AvailableLanguages.Add(new LanguageInfo
                        {
                            Code = code,
                            DisplayName = data["Languages"],
                            NativeName = data["Languages"]
                        });
                        Logger.Info($"Found language: {code} ({data["Languages"]})", "Lang");
                    }
                    else
                    {
                        Logger.Warn($"Skipping {Path.GetFileName(file)}: missing Languages key", "Lang");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to load {Path.GetFileName(file)}: {ex.Message}", "Lang");
                }
            }

            Logger.Info($"Available languages: {AvailableLanguages.Count}", "Lang");
        }

        /// <summary>
        /// Extracts bundled language JSON files from embedded resources to the Lang directory.
        /// Only extracts files that don't already exist on disk, so user modifications are preserved.
        /// </summary>
        private void ExtractDefaultLanguages()
        {
            try
            {
                Directory.CreateDirectory(LangDir);
                var assembly = Assembly.GetExecutingAssembly();
                var resourceNames = assembly.GetManifestResourceNames()
                    .Where(r => r.StartsWith(EmbeddedResourcePrefix) && r.EndsWith(".json"));

                foreach (var resourceName in resourceNames)
                {
                    var fileName = resourceName.Substring(EmbeddedResourcePrefix.Length);
                    var filePath = Path.Combine(LangDir, fileName);

                    if (File.Exists(filePath))
                    {
                        Logger.Debug($"Skip extract: {fileName} (exists)", "Lang");
                        continue;
                    }

                    using var stream = assembly.GetManifestResourceStream(resourceName);
                    if (stream == null)
                    {
                        Logger.Warn($"Embedded resource stream null: {resourceName}", "Lang");
                        continue;
                    }

                    using var reader = new StreamReader(stream);
                    var content = reader.ReadToEnd();
                    File.WriteAllText(filePath, content);
                    Logger.Info($"Extracted default language: {fileName}", "Lang");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to extract default languages: {ex.Message}", "Lang");
            }
        }

        public void LoadLanguage(string langCode)
        {
            // Load fallback first
            if (_fallbackStrings.Count == 0)
                _fallbackStrings = LoadJson(FallbackLang);

            _currentStrings = LoadJson(langCode);
            CurrentLangCode = langCode;

            App.Current.DataModel.ReadLang = MapToLegacyCode(langCode);
            ApplyToModel(App.Current.Language);

            Logger.Info($"Loaded {langCode}: {_currentStrings.Count} keys", "Lang");
        }

        public void SwitchLanguage(string langCode)
        {
            LoadLanguage(langCode);
            App.Current.LangList = AvailableLanguages.Select(l => new LanguageListModel
            {
                LangID = l.Code,
                LangVersion = "2.0",
                LangName = l.NativeName,
                LangFileName = l.Code
            }).ToList();
        }

        public string GetString(string key)
        {
            if (_currentStrings.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
                return value;
            if (_fallbackStrings.TryGetValue(key, out var fallback) && !string.IsNullOrEmpty(fallback))
                return fallback;
            Logger.Warn($"Missing key: {key}", "Lang");
            return key;
        }

        private Dictionary<string, string> LoadJson(string langCode)
        {
            var path = Path.Combine(LangDir, $"{langCode}.json");
            if (!File.Exists(path))
            {
                Logger.Warn($"Lang file not found: {path}", "Lang");
                return new Dictionary<string, string>();
            }
            try
            {
                var json = File.ReadAllText(path);
                var data = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                Logger.Debug($"Loaded {Path.GetFileName(path)}: {data?.Count ?? 0} entries", "Lang");
                return data ?? new Dictionary<string, string>();
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to parse {Path.GetFileName(path)}: {ex.Message}", "Lang");
                return new Dictionary<string, string>();
            }
        }

        private void ApplyToModel(LanguageModel model)
        {
            if (model == null) return;
            var props = typeof(LanguageModel).GetProperties();
            foreach (var prop in props)
            {
                if (prop.CanWrite && prop.PropertyType == typeof(string))
                {
                    var value = GetString(prop.Name);
                    prop.SetValue(model, value);
                }
            }
        }

        private string DetectSystemLanguage()
        {
            var culture = CultureInfo.CurrentUICulture;
            Logger.Debug($"System culture: {culture.Name} ({culture.DisplayName})", "Lang");

            var result = culture.Name switch
            {
                string n when n.StartsWith("zh-CN") || n.StartsWith("zh-Hans") => "zh-CN",
                string n when n.StartsWith("zh-TW") || n.StartsWith("zh-HK") || n.StartsWith("zh-Hant") => "zh-TW",
                string n when n.StartsWith("ja") => "ja",
                string n when n.StartsWith("en") => "en",
                _ => FallbackLang
            };

            Logger.Debug($"Detected language: {culture.Name} -> {result}", "Lang");
            return result;
        }

        private string MapLegacyCode(string legacy)
        {
            return legacy switch
            {
                "Lang_CN" => "zh-CN",
                "Lang_TW" => "zh-TW",
                "Lang_EN" => "en",
                "Lang_JP" => "ja",
                _ => legacy
            };
        }

        private string MapToLegacyCode(string code)
        {
            return code switch
            {
                "zh-CN" => "Lang_CN",
                "zh-TW" => "Lang_TW",
                "en" => "Lang_EN",
                "ja" => "Lang_JP",
                _ => code
            };
        }
    }

    public class LanguageInfo
    {
        public string Code { get; set; }
        public string DisplayName { get; set; }
        public string NativeName { get; set; }
    }
}
