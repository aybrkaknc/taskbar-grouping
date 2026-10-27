using System;
using System.IO;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaskbarFolders.Core.Groups;
using TaskbarFolders.Core.Icons;
using TaskbarFolders.Core.Shortcuts;
using TaskbarFolders.Manager;
using TaskbarFolders.Manager.Services;
using TaskbarFolders.Manager.ViewModels;
using TaskbarFolders.Manager.Views;
using TaskbarFolders.Shared.Configuration;
using Xunit;

namespace TaskbarFolders.Manager.Tests;

/// <summary>
/// Smoke-validates the Manager's DI graph. Catches misregistrations that would otherwise
/// only surface at App.OnStartup (DI mismatch, missing registration, lifetime conflict)
/// and lets the build fail fast in CI.
/// </summary>
public sealed class CompositionRootTests : IDisposable
{
    private readonly string _tempBase;

    public CompositionRootTests()
    {
        // Temp-rooted path provider so a future constructor that ever starts touching disk
        // (a cache hydration, say) cannot mutate the developer's real %APPDATA%.
        _tempBase = Path.Combine(Path.GetTempPath(), "TaskbarFolders.MgrComp." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempBase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempBase))
        {
            Directory.Delete(_tempBase, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(); // satisfy ILogger<T> dependencies
        services.AddTaskbarFoldersManager();
        // Replace the default %APPDATA%-rooted provider with a temp-rooted one for tests.
        services.AddSingleton<IAppDataPathProvider>(new AppDataPathProvider(_tempBase));
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    [Theory]
    // Persistence
    [InlineData(typeof(IAppDataPathProvider))]
    [InlineData(typeof(IGroupConfigStore))]
    [InlineData(typeof(IAppSettingsStore))]
    // Icon engine
    [InlineData(typeof(IIconExtractor))]
    [InlineData(typeof(ICompositeIconGenerator))]
    [InlineData(typeof(IIcoFileWriter))]
    [InlineData(typeof(IIconCache))]
    // Manager services
    [InlineData(typeof(IAutoStartService))]
    [InlineData(typeof(ISystemThemeProbe))]
    [InlineData(typeof(IThemeService))]
    [InlineData(typeof(IShortcutGenerator))]
    [InlineData(typeof(IShellChangeNotifier))]
    [InlineData(typeof(ILauncherPathResolver))]
    [InlineData(typeof(IGroupSyncService))]
    [InlineData(typeof(IUserConfirmation))]
    [InlineData(typeof(IProcessRunner))]
    [InlineData(typeof(IPinToTaskbarService))]
    // View models — all resolvable means every constructor dependency is also registered
    [InlineData(typeof(MainWindowViewModel))]
    [InlineData(typeof(GroupEditorViewModel))]
    [InlineData(typeof(SettingsViewModel))]
    public void EverySingletonAndViewModel_Resolves(System.Type serviceType)
    {
        using var provider = BuildProvider();

        var instance = provider.GetRequiredService(serviceType);

        instance.Should().NotBeNull();
    }

    [Fact]
    public void ValidateOnBuild_DetectsAnyLifetimeMismatch()
    {
        // BuildServiceProvider(ValidateOnBuild=true) throws if a singleton captures a
        // transient or scoped dependency. This test just exercises that build path —
        // a failure here is caught at provider construction, not at first GetService.
        var act = BuildProvider;

        act.Should().NotThrow();
    }

    [Fact]
    public void GroupEditorAndMainWindowViewModel_ResolveToSameSingletonInstance()
    {
        using var provider = BuildProvider();

        // MainWindowViewModel exposes Editor via constructor injection.
        // The editor itself is also registered as singleton — both resolutions must point
        // at the same instance, otherwise the sidebar selection event would dispatch to
        // an orphan editor whose state never reaches the view.
        var directly = provider.GetRequiredService<GroupEditorViewModel>();
        var viaMain = provider.GetRequiredService<MainWindowViewModel>().Editor;

        viaMain.Should().BeSameAs(directly);
    }

    [Fact]
    public void SettingsViewModel_ResolvesToTheSameInstanceEveryTime()
    {
        using var provider = BuildProvider();

        // The settings handler resolves a SettingsViewModel, loads it, and then resolves
        // SettingsWindow - which takes its own SettingsViewModel through the constructor.
        // While the registration was transient those were two different instances: the
        // loaded one was discarded, the dialog bound an unloaded one showing constructor
        // defaults, and Save persisted those defaults over the user's settings.json.
        // Constructing the window itself would need an STA thread, so the lifetime is
        // what this guards.
        var loadedByCaller = provider.GetRequiredService<SettingsViewModel>();
        var injectedIntoWindow = provider.GetRequiredService<SettingsViewModel>();

        injectedIntoWindow.Should().BeSameAs(loadedByCaller);
    }
}
