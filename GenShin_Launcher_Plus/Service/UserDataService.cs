using GenShin_Launcher_Plus.Models;
using GenShin_Launcher_Plus.Service.IService;
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace GenShin_Launcher_Plus.Service
{
    public class UserDataService : IUserDataService
    {
        public static string BuildAccountFileName(string gameBiz, string displayName)
        {
            var biz = SanitizeFilePart(gameBiz);
            var name = SanitizeFilePart(displayName);
            return $"{biz}_{name}";
        }

        public static string GetAccountDisplayName(string fileName)
        {
            try
            {
                var path = Path.Combine("UserData", fileName);
                if (!File.Exists(path)) return fileName;
                var registry = JsonConvert.DeserializeObject<RegistryModel>(File.ReadAllText(path));
                return string.IsNullOrWhiteSpace(registry?.Name) ? fileName : registry.Name;
            }
            catch
            {
                return fileName;
            }
        }

        private static string SanitizeFilePart(string? value)
        {
            value = (value ?? string.Empty).Trim();
            foreach (var c in Path.GetInvalidFileNameChars())
                value = value.Replace(c, '_');
            return string.IsNullOrWhiteSpace(value) ? "account" : value;
        }

        /// <summary>
        /// 读取用户数据列表
        /// </summary>
        /// <returns></returns>
        public List<UserListModel> ReadUserList()
        {
            List<UserListModel> list = new();
            DirectoryInfo TheFolder = new(@"UserData");
            if (!TheFolder.Exists)
                TheFolder.Create();
            foreach (FileInfo NextFile in TheFolder.GetFiles())
            {
                var item = new UserListModel
                {
                    UserName = NextFile.Name,
                    DisplayName = NextFile.Name,
                };
                try
                {
                    var registry = JsonConvert.DeserializeObject<RegistryModel>(File.ReadAllText(NextFile.FullName));
                    if (registry != null)
                    {
                        item.DisplayName = string.IsNullOrWhiteSpace(registry.Name) ? NextFile.Name : registry.Name;
                        item.GameBiz = registry.GameBiz;
                        item.Port = registry.Port;
                        item.Uid = registry.Uid;
                        item.SavedAt = registry.SavedAt;
                    }
                }
                catch { }
                item.IsCurrent = string.Equals(App.Current?.DataModel?.SwitchUser, item.UserName, System.StringComparison.OrdinalIgnoreCase);
                list.Add(item);
            }
            return list;
        }
    }
}
