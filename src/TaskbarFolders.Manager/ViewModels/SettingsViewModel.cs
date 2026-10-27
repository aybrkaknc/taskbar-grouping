using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TaskbarFolders.Manager.Services;
using TaskbarFolders.Shared.Configuration;
using TaskbarFolders.Shared.Models;

namespace TaskbarFolders.Manager.ViewModels;

/// <summary>
/// Backing view model for the Settings window. Loads the current <see cref="AppSettings"/>
/// and the registry-backed auto-start state, tracks unsaved edits, and reconciles both on
/// <see cref="SaveCommand"/>.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IAppSettingsStore _store;
    private readonly IAutoStartService _autoStartService;
    private readonly IThemeService _themeService;
    private readonly IShellIntegrationRemover _remover;
    private readonly IUserConfirmation _confirmation;
    private readonly ILogger<SettingsViewModel>? _logger;

    private bool _suppressDirtyTracking;

    /// <summary>Initializes a new instance.</summary>
    public SettingsViewModel(
        IAppSettingsStore store,
        IAutoStartService autoStart,
        IThemeService themeService,
        IShellIntegrationRemover remover,
        IUserConfirmation confirmation,
        ILogger<SettingsViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(autoStart);
        ArgumentNullException.ThrowIfNull(themeService);
        ArgumentNullException.ThrowIfNull(remover);
        ArgumentNullException.ThrowIfNull(confirmation);

        _store = store;
        _autoStartService = autoStart;
        _themeService = themeService;
        _remover = remover;
        _confirmation = confirmation;
        _logger = logger;
    }

    [ObservableProperty]
    private ThemePreference _theme;

    [ObservableProperty]
    private bool _autoStart;

    [ObservableProperty]
    private bool _enableAnimations = true;

    [ObservableProperty]
    private PopupPositionPreference _popupPosition;

    /// <summary>Whether Explorer's context menu offers "Taskbar Groups" for .exe and .lnk files.</summary>
    [ObservableProperty]
    private bool _showInExplorerMenu = true;

    /// <summary>Raised after the settings have been written.</summary>
    public event EventHandler? Saved;

    /// <summary>
    /// Raised after <see cref="RemoveFromThisPcCommand"/> has run. The Manager closes then:
    /// staying open, it would write the Start menu entries straight back on the next sync.
    /// </summary>
    public event EventHandler? RemovedFromThisPc;

    /// <summary>Whether any property has been mutated since the last load or save.</summary>
    [ObservableProperty]
    private bool _hasUnsavedChanges;

    /// <summary>Loads the current settings into the view model and resets the dirty flag.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _store.LoadAsync(cancellationToken).ConfigureAwait(true);

        _suppressDirtyTracking = true;
        try
        {
            Theme = settings.Theme;
            // The registry is the source of truth for auto-start — settings.AutoStart is
            // mirrored on Save, but if the user removed the run entry by hand the registry wins.
            AutoStart = _autoStartService.IsEnabled;
            EnableAnimations = settings.EnableAnimations;
            ShowInExplorerMenu = settings.ShowInExplorerMenu;
            PopupPosition = settings.PopupPosition;
        }
        finally
        {
            _suppressDirtyTracking = false;
            HasUnsavedChanges = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var settings = new AppSettings
        {
            Theme = Theme,
            AutoStart = AutoStart,
            EnableAnimations = EnableAnimations,
            ShowInExplorerMenu = ShowInExplorerMenu,
            PopupPosition = PopupPosition,
        };

        await _store.SaveAsync(settings).ConfigureAwait(true);

        if (AutoStart)
        {
            _autoStartService.Enable();
        }
        else
        {
            _autoStartService.Disable();
        }

        _themeService.SetPreference(Theme);

        HasUnsavedChanges = false;
        _logger?.LogInformation("Settings saved. Theme={Theme} AutoStart={AutoStart} Animations={Animations} Position={Position}.",
            Theme, AutoStart, EnableAnimations, PopupPosition);
        Saved?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task RemoveFromThisPcAsync()
    {
        if (!_confirmation.Confirm(
                "Remove from this PC",
                "This removes the \"Taskbar Groups\" menu, the start-with-Windows entry, and every group's " +
                "shortcuts, icons and Start menu entries, then closes the Manager. Your groups are kept. " +
                "Pinned taskbar tiles stay until you unpin them.\n\nContinue?"))
        {
            return;
        }

        var complete = await _remover.RemoveAsync().ConfigureAwait(true);
        _confirmation.Notify("Remove from this PC", RemovalResultMessage(complete));
        RemovedFromThisPc?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>What to tell the user after a removal, from the Settings button or the command line.</summary>
    /// <param name="complete">Result of <see cref="IShellIntegrationRemover.RemoveAsync"/>.</param>
    public static string RemovalResultMessage(bool complete) => complete
        ? "Done. Unpin the group tiles from the taskbar, then the TaskbarFolders folder can be deleted."
        : "Some entries could not be removed; see %APPDATA%\\TaskbarFolders\\logs for details. " +
          "Unpin the group tiles from the taskbar before deleting the folder.";

    partial void OnThemeChanged(ThemePreference value) => MarkDirty();
    partial void OnAutoStartChanged(bool value) => MarkDirty();
    partial void OnEnableAnimationsChanged(bool value) => MarkDirty();
    partial void OnShowInExplorerMenuChanged(bool value) => MarkDirty();
    partial void OnPopupPositionChanged(PopupPositionPreference value) => MarkDirty();

    private void MarkDirty()
    {
        if (!_suppressDirtyTracking)
        {
            HasUnsavedChanges = true;
        }
    }
}
