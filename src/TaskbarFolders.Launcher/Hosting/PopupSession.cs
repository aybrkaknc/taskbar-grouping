using System;
using System.Windows;
using Microsoft.Extensions.Logging;
using TaskbarFolders.Core.Groups;
using TaskbarFolders.Core.Icons;
using TaskbarFolders.Launcher.Configuration;
using TaskbarFolders.Launcher.Services;
using TaskbarFolders.Launcher.ViewModels;
using TaskbarFolders.Launcher.Views;
using TaskbarFolders.Shared.Configuration;
using TaskbarFolders.Shared.Models;

namespace TaskbarFolders.Launcher.Hosting;

/// <summary>
/// Everything one open popup owns: its group, the cursor anchor of the click that opened it, the
/// view model and the file adder for that group, and (once <see cref="CreateWindow"/> is called)
/// the window. A resident launcher makes one per popup, so no state outlives the popup it
/// belongs to; the services shared between popups stay in the DI container.
/// </summary>
public sealed class PopupSession : IDisposable
{
    private readonly ITaskbarPositionHelper _positionHelper;
    private readonly AppSettings _settings;

    internal PopupSession(
        string groupId,
        Point anchor,
        PopupViewModel viewModel,
        CommandLineAppAdder adder,
        ITaskbarPositionHelper positionHelper,
        AppSettings settings)
    {
        GroupId = groupId;
        Anchor = anchor;
        ViewModel = viewModel;
        Adder = adder;
        _positionHelper = positionHelper;
        _settings = settings;
    }

    /// <summary>The group this popup shows.</summary>
    public string GroupId { get; }

    /// <summary>Cursor position of the click that opened the popup, in device pixels.</summary>
    public Point Anchor { get; }

    /// <summary>The popup's view model.</summary>
    public PopupViewModel ViewModel { get; }

    /// <summary>Adds files to this session's group.</summary>
    public CommandLineAppAdder Adder { get; }

    /// <summary>The popup window; <see langword="null"/> until <see cref="CreateWindow"/>.</summary>
    public PopupWindow? Window { get; private set; }

    /// <summary>Builds the popup window. Must run on the UI thread, after the view model is loaded.</summary>
    /// <returns>The new, not yet shown window.</returns>
    public PopupWindow CreateWindow() => Window ??= new PopupWindow(ViewModel, _positionHelper, _settings);

    /// <inheritdoc/>
    public void Dispose() => ViewModel.Dispose();
}

/// <summary>
/// Builds <see cref="PopupSession"/>s from the long-lived services of the resident launcher. The
/// per-popup objects are composed here, by hand, because each takes the group and the click's anchor
/// as its own state; registering them as scoped services would only move the same wiring into the
/// container.
/// </summary>
public sealed class PopupSessionFactory
{
    private readonly IGroupConfigStore _store;
    private readonly IIconExtractor _extractor;
    private readonly IIconCache _cache;
    private readonly IProcessLauncher _launcher;
    private readonly IGroupSyncService _syncService;
    private readonly ILoggerFactory _loggers;
    private readonly PerfRecorder _perf;

    /// <summary>Initializes a new instance.</summary>
    public PopupSessionFactory(
        IGroupConfigStore store,
        IIconExtractor extractor,
        IIconCache cache,
        IProcessLauncher launcher,
        IGroupSyncService syncService,
        ILoggerFactory loggers,
        PerfRecorder perf)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(extractor);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(syncService);
        ArgumentNullException.ThrowIfNull(loggers);
        ArgumentNullException.ThrowIfNull(perf);

        _store = store;
        _extractor = extractor;
        _cache = cache;
        _launcher = launcher;
        _syncService = syncService;
        _loggers = loggers;
        _perf = perf;
    }

    /// <summary>Creates the session for one popup.</summary>
    /// <param name="groupId">The group to show.</param>
    /// <param name="settings">The settings read for this popup.</param>
    /// <param name="anchor">The click's cursor position, in device pixels.</param>
    /// <returns>The session.</returns>
    public PopupSession Create(string groupId, AppSettings settings, Point anchor)
    {
        ArgumentException.ThrowIfNullOrEmpty(groupId);
        ArgumentNullException.ThrowIfNull(settings);

        var options = new LauncherOptions(groupId);
        var cursor = new LauncherCursorAnchor();
        cursor.Seed(anchor);

        var viewModel = new PopupViewModel(
            _store,
            _extractor,
            _cache,
            _launcher,
            _syncService,
            options,
            _loggers.CreateLogger<PopupViewModel>(),
            _perf);
        var adder = new CommandLineAppAdder(_store, _syncService, options, _loggers.CreateLogger<CommandLineAppAdder>());

        return new PopupSession(groupId, anchor, viewModel, adder, new TaskbarPositionHelper(cursor), settings);
    }
}
