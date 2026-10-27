using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using TaskbarFolders.Core.Groups;
using TaskbarFolders.Core.Icons;
using TaskbarFolders.Core.Shortcuts;
using TaskbarFolders.Manager.Services;
using TaskbarFolders.Manager.ViewModels;
using TaskbarFolders.Manager.Views;
using TaskbarFolders.Shared.Configuration;

namespace TaskbarFolders.Manager;

/// <summary>
/// Centralised service-registration helper. Extracted from <see cref="App.OnStartup"/> so
/// the same registration graph can be exercised by composition tests without spinning up
/// WPF, the generic host, or the log file pipeline.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ManagerServiceCollectionExtensions
{
    /// <summary>
    /// Registers every Manager-side service, view model, and view used by the running app.
    /// Call from either the generic host's <c>ConfigureServices</c> hook (production) or a
    /// raw <see cref="ServiceCollection"/> in tests.
    /// </summary>
    public static IServiceCollection AddTaskbarFoldersManager(this IServiceCollection services)
    {
        // Persistence — singletons because the stores carry no per-call state and the
        // path provider is rooted at %APPDATA% for the lifetime of the process.
        services.AddSingleton<IAppDataPathProvider, AppDataPathProvider>();
        services.AddSingleton<IGroupConfigStore, JsonGroupConfigStore>();
        services.AddSingleton<IAppSettingsStore, JsonAppSettingsStore>();

        // Icon engine — singletons; ShellIconExtractor is stateless and the cache
        // (M2.4) sits in front of IIconExtractor for repeated extractions.
        services.AddSingleton<IIconExtractor, ShellIconExtractor>();
        services.AddSingleton<ICompositeIconGenerator, CompositeIconGenerator>();
        services.AddSingleton<IIcoFileWriter, IcoFileWriter>();
        services.AddSingleton<IIconCache, FileSystemIconCache>();

        // Manager-side services.
        services.AddSingleton<IAutoStartService, RegistryAutoStartService>();
        services.AddSingleton<ISystemThemeProbe, RegistrySystemThemeProbe>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IShortcutGenerator, ShortcutGenerator>();
        services.AddSingleton<IShortcutReader, ShortcutReader>();
        services.AddSingleton<IShellChangeNotifier, ShellChangeNotifier>();
        services.AddSingleton<ILauncherPathResolver, LauncherPathResolver>();
        services.AddSingleton<IGroupSyncService, GroupSyncService>();
        services.AddSingleton<IUserConfirmation, MessageBoxUserConfirmation>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IPinToTaskbarService, LauncherProcessPinService>();

        // View models — MainWindow is itself a singleton conceptually (one main window per
        // process), so the backing VM is singleton too. App.OnStartup loads groups into it
        // before showing the window.
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<GroupEditorViewModel>();
        // Singleton on purpose: the settings dialog is opened by resolving SettingsWindow,
        // which takes its view model through the constructor. Under a transient registration
        // the caller and the window received two different instances, so the instance the
        // caller had loaded was discarded and the dialog bound an unloaded one - showing
        // defaults and writing them over settings.json on Save. LoadAsync re-reads on every
        // open, so sharing one instance across dialog sessions carries no stale state.
        services.AddSingleton<SettingsViewModel>();

        // Views — transient so each Show creates a fresh window instance.
        services.AddTransient<MainWindow>();
        services.AddTransient<SettingsWindow>();

        return services;
    }
}
