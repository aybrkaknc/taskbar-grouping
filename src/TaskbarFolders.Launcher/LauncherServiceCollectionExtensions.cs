using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using TaskbarFolders.Core.Groups;
using TaskbarFolders.Core.Icons;
using TaskbarFolders.Core.Shortcuts;
using TaskbarFolders.Launcher.Configuration;
using TaskbarFolders.Launcher.Services;
using TaskbarFolders.Launcher.ViewModels;
using TaskbarFolders.Launcher.Views;
using TaskbarFolders.Shared.Configuration;

namespace TaskbarFolders.Launcher;

/// <summary>
/// Centralised service-registration helper. Extracted from <see cref="App.OnStartup"/> so
/// the same registration graph can be exercised by composition tests without parsing CLI
/// args or spawning real processes.
/// </summary>
/// <remarks>
/// Annotation bumped from <c>"windows"</c> to <c>"windows10.0.19041.0"</c> in v0.4 because
/// <see cref="TaskbarPinRunner"/> uses WinRT TaskbarManager which requires Win10 1903+.
/// The project TFM <c>net8.0-windows10.0.19041.0</c> enforces this anyway; the explicit
/// attribute lets CA1416 verify it.
/// </remarks>
[SupportedOSPlatform("windows10.0.19041.0")]
public static class LauncherServiceCollectionExtensions
{
    /// <summary>
    /// Registers every Launcher-side service, view model, and view used by the running app.
    /// Caller supplies <paramref name="options"/> + <paramref name="paths"/> so production
    /// (which derives these from the command line and <c>%APPDATA%</c>) and tests (which
    /// inject fixtures) share the same registration list.
    /// </summary>
    public static IServiceCollection AddTaskbarFoldersLauncher(
        this IServiceCollection services,
        LauncherOptions options,
        IAppDataPathProvider paths)
    {
        services.AddSingleton(options);
        services.AddSingleton(paths);

        // Persistence. Settings are read-only here; the popup writes its own group's config
        // when apps are added or removed in it (ADR-005).
        services.AddSingleton<IGroupConfigStore, JsonGroupConfigStore>();
        services.AddSingleton<IAppSettingsStore, JsonAppSettingsStore>();

        // Icon engine — needed for the popup tiles.
        services.AddSingleton<IIconExtractor, ShellIconExtractor>();
        services.AddSingleton<IIconCache, FileSystemIconCache>();

        // Group sync — regenerates the composite icon, shortcut and Start menu anchor after
        // the popup edits its group. The launcher is the shortcut target, hence the resolver.
        services.AddSingleton<ICompositeIconGenerator, CompositeIconGenerator>();
        services.AddSingleton<IIcoFileWriter, IcoFileWriter>();
        services.AddSingleton<IShortcutGenerator, ShortcutGenerator>();
        services.AddSingleton<IShellChangeNotifier, ShellChangeNotifier>();
        services.AddSingleton<ILauncherPathResolver, SelfLauncherPathResolver>();
        services.AddSingleton<IGroupSyncService, GroupSyncService>();
        services.AddSingleton<CommandLineAppAdder>();

        // Launcher-only services.
        services.AddSingleton<IProcessLauncher, ProcessLauncher>();
        services.AddSingleton<ICursorAnchor, LauncherCursorAnchor>();
        services.AddSingleton<ITaskbarPositionHelper, TaskbarPositionHelper>();
        services.AddSingleton<IShortcutReader, ShortcutReader>();
        services.AddSingleton<PinVerifier>();
        services.AddSingleton<TaskbarPinRunner>();

        services.AddSingleton<PopupViewModel>();
        services.AddTransient<PopupWindow>();
        services.AddTransient<PinHostWindow>();

        return services;
    }
}
