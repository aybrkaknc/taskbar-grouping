using System;
using System.IO;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaskbarFolders.Core.Icons;
using TaskbarFolders.Core.Shortcuts;
using TaskbarFolders.Launcher;
using TaskbarFolders.Launcher.Configuration;
using TaskbarFolders.Launcher.Hosting;
using TaskbarFolders.Launcher.Services;
using TaskbarFolders.Launcher.ViewModels;
using TaskbarFolders.Shared.Configuration;
using TaskbarFolders.Shared.Models;
using Xunit;

namespace TaskbarFolders.Launcher.Tests;

/// <summary>
/// Smoke-validates the Launcher's DI graph. Same role as the Manager equivalent — catches
/// misregistrations at build time rather than at first run on a user's machine.
/// </summary>
public sealed class CompositionRootTests : IDisposable
{
    private readonly string _tempBase;
    private readonly LauncherOptions _options;
    private readonly AppDataPathProvider _paths;

    public CompositionRootTests()
    {
        _tempBase = Path.Combine(Path.GetTempPath(), "TaskbarFolders.LauncherComp." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempBase);

        _options = new LauncherOptions("test-group-id");
        _paths = new AppDataPathProvider(_tempBase);
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
        services.AddLogging();
        services.AddTaskbarFoldersLauncher(_options, _paths);
        // v0.3+: PopupWindow takes AppSettings as a ctor param. Production registers it
        // post-load in App.OnStartup; here we register a default instance so ValidateOnBuild
        // can resolve the full graph.
        services.AddSingleton(new AppSettings());
        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        // Pre-seed the cursor anchor so any future composition test that calls
        // TaskbarPositionHelper.ComputePlacement does not throw with a confusing
        // "read before Seed" message. Production seeds in App.OnStartup; tests would
        // otherwise have to remember to do the same.
        provider.GetRequiredService<ICursorAnchor>().Seed(new System.Windows.Point(0, 0));
        return provider;
    }

    [Theory]
    [InlineData(typeof(IGroupConfigStore))]
    [InlineData(typeof(IAppSettingsStore))]
    [InlineData(typeof(IIconExtractor))]
    [InlineData(typeof(IIconCache))]
    [InlineData(typeof(IProcessLauncher))]
    [InlineData(typeof(ICursorAnchor))]
    [InlineData(typeof(ITaskbarPositionHelper))]
    [InlineData(typeof(TaskbarPinRunner))]
    [InlineData(typeof(PinVerifier))]
    [InlineData(typeof(IShortcutReader))]
    [InlineData(typeof(PopupViewModel))]
    [InlineData(typeof(PopupSessionFactory))]
    [InlineData(typeof(LauncherOptions))]
    [InlineData(typeof(IAppDataPathProvider))]
    public void EveryRegisteredService_Resolves(Type serviceType)
    {
        using var provider = BuildProvider();

        var instance = provider.GetRequiredService(serviceType);

        instance.Should().NotBeNull();
    }

    [Fact]
    public void LauncherOptions_ResolvesToInstancePassedToExtension()
    {
        using var provider = BuildProvider();

        var resolved = provider.GetRequiredService<LauncherOptions>();

        resolved.Should().BeSameAs(_options);
        resolved.GroupId.Should().Be("test-group-id");
    }

    [Fact]
    public void AppDataPathProvider_ResolvesToInstancePassedToExtension()
    {
        using var provider = BuildProvider();

        var resolved = provider.GetRequiredService<IAppDataPathProvider>();

        resolved.Should().BeSameAs(_paths);
        resolved.AppDataRoot.Should().StartWith(_tempBase);
    }

    [Fact]
    public void ValidateOnBuild_PassesForLauncherGraph()
    {
        var act = BuildProvider;
        act.Should().NotThrow();
    }
}
