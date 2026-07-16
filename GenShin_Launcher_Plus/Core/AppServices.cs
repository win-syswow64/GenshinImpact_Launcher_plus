using System;
using System.Collections.Concurrent;
using GenShin_Launcher_Plus.Service;
using GenShin_Launcher_Plus.Service.IService;
using GenShin_Launcher_Plus.ViewModels;

namespace GenShin_Launcher_Plus.Core
{
    /// <summary>
    /// Application composition root.  Keeping service construction here makes
    /// feature code independent from concrete service types and gives the
    /// launcher one place to evolve towards a full DI container.
    /// </summary>
    public sealed class AppServices
    {
        private readonly ConcurrentDictionary<Type, object> _singletons = new();

        private AppServices(ILauncherSession session)
        {
            RegisterSingleton(session);
            var userDataService = new UserDataService();
            var registryService = new RegistryService();
            RegisterSingleton<IUserDataService>(userDataService);
            RegisterSingleton<IRegistryService>(registryService);
            RegisterSingleton(new LoadProgramCore(session, registryService));
            RegisterSingleton<ILaunchService>(new LaunchService(session, userDataService));
            RegisterSingleton<IUpdateService>(new UpdateService());
            RegisterSingleton<ISettingService>(new SettingService());
        }

        public static AppServices CreateDefault(ILauncherSession session) => new(session);

        public T GetRequiredService<T>() where T : class
        {
            if (_singletons.TryGetValue(typeof(T), out var service))
                return (T)service;

            throw new InvalidOperationException($"Service '{typeof(T).FullName}' is not registered.");
        }

        public GameInstallService CreateGameInstallService() => new(GetRequiredService<ILauncherSession>());

        public IMainWindowService CreateMainWindowService(MainWindow window, MainWindowViewModel viewModel) => new MainService(GetRequiredService<ILauncherSession>(), window, viewModel);

        public ILauncherNavigationService CreateLauncherNavigationService() => new LauncherNavigationService();

        public MainWindowViewModel CreateMainWindowViewModel(MainWindow window) => new(
            window,
            GetRequiredService<IUpdateService>(),
            GetRequiredService<ILaunchService>(),
            GetRequiredService<LoadProgramCore>(),
            GetRequiredService<ILauncherSession>(),
            CreateGameInstallService(),
            CreateMainWindowService,
            CreateLauncherNavigationService());

        public HomePageViewModel CreateHomePageViewModel() => new(
            GetRequiredService<ILaunchService>(),
            GetRequiredService<ILauncherSession>(),
            CreateGameInstallService);

        public UsersPageViewModel CreateUsersPageViewModel() => new(
            GetRequiredService<IRegistryService>(),
            GetRequiredService<IUserDataService>());

        public UpdatePageViewModel CreateUpdatePageViewModel() => new(GetRequiredService<IUpdateService>());

        public SettingsPageViewModel CreateSettingsPageViewModel(int mode) => new(
            mode,
            GetRequiredService<ISettingService>(),
            GetRequiredService<IUserDataService>(),
            GetRequiredService<IRegistryService>(),
            GetRequiredService<IUpdateService>(),
            CreateGameInstallService,
            CreateMainWindowService);

        private void RegisterSingleton<T>(T service) where T : class
        {
            if (!_singletons.TryAdd(typeof(T), service))
                throw new InvalidOperationException($"Service '{typeof(T).FullName}' is already registered.");
        }
    }
}
