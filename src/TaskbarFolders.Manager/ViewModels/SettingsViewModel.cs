using System;
using System.Diagnostics;
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
    private readonly InstallLocation _install;
    private readonly IProcessRunner _processes;
    private readonly IUserConfirmation _confirmation;
    private readonly ILogger<SettingsViewModel>? _logger;

    private bool _suppressDirtyTracking;

    /// <summary>Initializes a new instance.</summary>
    public SettingsViewModel(
        IAppSettingsStore store,
        IAutoStartService autoStart,
        IThemeService themeService,
        InstallLocation install,
        IProcessRunner processes,
        IUserConfirmation confirmation,
        ILogger<SettingsViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(autoStart);
        ArgumentNullException.ThrowIfNull(themeService);
        ArgumentNullException.ThrowIfNull(install);
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(confirmation);

        _store = store;
        _autoStartService = autoStart;
        _themeService = themeService;
        _install = install;
        _processes = processes;
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
    /// Raised once <see cref="UninstallCommand"/> has started the uninstaller. The Manager exits
    /// then: its executable cannot be deleted while it runs.
    /// </summary>
    public event EventHandler? UninstallStarted;

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

    /// <summary>
    /// Starts the uninstaller that came with this copy — the installer's, or the portable
    /// <c>Uninstall.cmd</c>. It asks for confirmation itself; the Manager then has to exit so
    /// that its own files can be deleted.
    /// </summary>
    [RelayCommand]
    private void Uninstall()
    {
        var uninstaller = _install.FindUninstaller();
        if (uninstaller is null)
        {
            _confirmation.Notify(
                "Uninstall",
                "No uninstaller was found next to this copy of TaskbarFolders. A development build has none; " +
                $"run TaskbarFolders.Manager.exe {UninstallArguments.Uninstall} to clean up instead.");
            return;
        }

        var psi = uninstaller.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
            ? new ProcessStartInfo("cmd.exe", $"/c \"\"{uninstaller}\"\"") { UseShellExecute = false, CreateNoWindow = true }
            : new ProcessStartInfo(uninstaller) { UseShellExecute = true };

        if (!_processes.TryStart(psi))
        {
            _logger?.LogWarning("Uninstall: could not start {Path}.", uninstaller);
            _confirmation.Notify("Uninstall", $"The uninstaller could not be started:\n{uninstaller}");
            return;
        }

        UninstallStarted?.Invoke(this, EventArgs.Empty);
    }

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
