using System;
using System.Linq;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaskbarFolders.Core.Icons;
using TaskbarFolders.Manager.Services;
using TaskbarFolders.Manager.ViewModels;
using TaskbarFolders.Manager.Views;
using TaskbarFolders.Shared.Configuration;
using TaskbarFolders.Shared.Logging;

namespace TaskbarFolders.Manager;

/// <summary>
/// Application entry point for the TaskbarFolders Manager.
/// </summary>
public partial class App : Application
{
    private readonly IHost _host;

    /// <summary>
    /// Initializes a new instance of the <see cref="App"/> class
    /// and constructs the generic host with the service container.
    /// </summary>
    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .UseContentRoot(AppContext.BaseDirectory)
            .ConfigureLogging((_, logging) =>
            {
                var paths = new AppDataPathProvider();
                logging.AddTaskbarFoldersFileLogging(paths.LogsDirectory, "manager");
            })
            .ConfigureServices((_, services) => services.AddTaskbarFoldersManager())
            // Match the production wiring to what the composition tests assert. ValidateOnBuild
            // surfaces missing registrations and lifetime mismatches at process start instead of
            // at first GetService — Host.CreateDefaultBuilder only enables this in Development.
            .UseDefaultServiceProvider((_, options) =>
            {
                options.ValidateOnBuild = true;
                options.ValidateScopes = true;
            })
            .Build();
    }

    /// <summary>
    /// Gets the application service provider. Available after construction.
    /// </summary>
    public IServiceProvider Services => _host.Services;

    /// <inheritdoc/>
    protected override async void OnStartup(StartupEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        await _host.StartAsync().ConfigureAwait(true);

        // Apply the persisted theme before any window is built so DynamicResource
        // bindings paint with the correct brushes on first render.
        var settingsStore = _host.Services.GetRequiredService<IAppSettingsStore>();
        var settings = await settingsStore.LoadAsync().ConfigureAwait(true);
        _host.Services.GetRequiredService<IThemeService>().SetPreference(settings.Theme);

        // Load groups before constructing the window so the sidebar binding shows data
        // immediately rather than flashing empty for one frame.
        var viewModel = _host.Services.GetRequiredService<MainWindowViewModel>();
        await viewModel.LoadGroupsAsync().ConfigureAwait(true);

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();

        // v0.4.1 heal-up: ensure every existing group has a Start Menu anchor .lnk so the
        // one-click pin via TaskbarManager.RequestPinCurrentAppAsync can persist the pin.
        // Run AFTER Show() so the user-visible first paint is not blocked. Iterate the
        // already-loaded VM groups instead of re-issuing a LoadAllAsync (avoids a duplicate
        // disk pass). No-op on subsequent launches (file already present).
        var sync = _host.Services.GetRequiredService<IGroupSyncService>();
        foreach (var groupVm in viewModel.Groups)
        {
            sync.EnsureStartMenuShortcut(groupVm.Config);
        }

        // Then the reverse direction: anchors left behind by deleted groups. Only adding
        // here was how a stale anchor could outlive its group indefinitely.
        sync.RemoveOrphanedStartMenuAnchors();

        // Deferred startup IO: prune stale icon-cache PNGs and old log files in the background
        // so the first paint of MainWindow is not blocked by ~10-70 ms of enumerate-and-delete.
        _host.Services.GetRequiredService<IIconCache>().StartBackgroundPrune();
        _host.Services.GetServices<ILoggerProvider>()
            .OfType<FileLoggerProvider>()
            .FirstOrDefault()?.StartBackgroundPrune();

        base.OnStartup(e);
    }

    /// <inheritdoc/>
    protected override async void OnExit(ExitEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        await _host.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
        _host.Dispose();

        base.OnExit(e);
    }
}
