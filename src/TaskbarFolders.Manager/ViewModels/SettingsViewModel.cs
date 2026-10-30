using System;
using System.Diagnostics;
using System.IO;
using System.Security;
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
/// and the registry-backed auto-start state. A setting takes effect and is stored as soon as it
/// changes: there is no Save step, so nothing can be left unsaved.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly IAppSettingsStore _store;
    private readonly IAutoStartService _autoStartService;
    private readonly IThemeService _themeService;
    private readonly InstallLocation _install;
    private readonly IProcessRunner _processes;
    private readonly IUserConfirmation _confirmation;
    private readonly ILogger<SettingsViewModel>? _logger;

    // Changes are applied one at a time. The store takes the whole document, so two overlapping
    // writes built from different moments would let the later one undo the earlier change.
    private readonly SemaphoreSlim _applyGate = new(1, 1);

    // Set while the view model changes a property itself — loading, or putting a value back —
    // which is not something to apply.
    private bool _suppressApply;

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

    /// <summary>The materials a popup can be drawn on, as the dialog lists them.</summary>
    public static IReadOnlyList<BackdropOption> PopupBackdropOptions { get; } =
        GroupEditorViewModel.BackdropOptions.Where(o => o.Value is not null).ToArray();

    /// <summary>What every popup is drawn on, unless its group chose otherwise on Appearance.</summary>
    [ObservableProperty]
    private PopupBackdrop _popupBackdrop;

    /// <summary>Whether Explorer's context menu offers "Taskbar Groups" for .exe and .lnk files.</summary>
    [ObservableProperty]
    private bool _showInExplorerMenu = true;

    /// <summary>
    /// Raised after a change has been written to the store, once per change — the Explorer menu
    /// is rewritten on it, and reads the setting back from the store.
    /// </summary>
    public event EventHandler? Saved;

    /// <summary>
    /// Raised once <see cref="UninstallCommand"/> has started the uninstaller. The Manager exits
    /// then: its executable cannot be deleted while it runs.
    /// </summary>
    public event EventHandler? UninstallStarted;

    /// <summary>Loads the current settings into the view model. Applies nothing.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _store.LoadAsync(cancellationToken).ConfigureAwait(true);

        _suppressApply = true;
        try
        {
            Theme = settings.Theme;
            // The registry is the source of truth for auto-start — settings.AutoStart is
            // mirrored on Save, but if the user removed the run entry by hand the registry wins.
            AutoStart = _autoStartService.IsEnabled;
            EnableAnimations = settings.EnableAnimations;
            ShowInExplorerMenu = settings.ShowInExplorerMenu;
            PopupPosition = settings.PopupPosition;
            PopupBackdrop = settings.PopupBackdrop;
        }
        finally
        {
            _suppressApply = false;
        }
    }

    /// <summary>Completes once every change made so far has been written.</summary>
    public async Task WhenIdleAsync()
    {
        await _applyGate.WaitAsync().ConfigureAwait(true);
        _applyGate.Release();
    }

    /// <inheritdoc />
    public void Dispose() => _applyGate.Dispose();

    // Never throws: nothing awaits the property setter that starts it, so a failure is logged and
    // shown to the user, and the setting stays as chosen in memory.
    private async Task ApplyAsync(string setting)
    {
        await _applyGate.WaitAsync().ConfigureAwait(true);
        try
        {
            // The registry first: a start-up entry that cannot be written must not be stored as
            // if it had been.
            if (setting == nameof(AutoStart) && !TryApplyAutoStart())
            {
                return;
            }

            // The store takes the whole document, and it holds more than this dialog edits.
            var stored = await _store.LoadAsync().ConfigureAwait(true);
            await _store.SaveAsync(new AppSettings
            {
                Theme = Theme,
                AutoStart = AutoStart,
                EnableAnimations = EnableAnimations,
                ShowInExplorerMenu = ShowInExplorerMenu,
                PopupPosition = PopupPosition,
                PopupBackdrop = PopupBackdrop,
                OrphanedTilesNoticeShown = stored.OrphanedTilesNoticeShown,
            }).ConfigureAwait(true);

            if (setting == nameof(Theme))
            {
                _themeService.SetPreference(Theme);
            }

            _logger?.LogInformation("Setting {Setting} applied. Theme={Theme} AutoStart={AutoStart} Animations={Animations} Position={Position}.",
                setting, Theme, AutoStart, EnableAnimations, PopupPosition);
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Could not store the {Setting} setting.", setting);
            _confirmation.Notify("Not saved", $"The setting could not be saved:\n{ex.Message}");
        }
        finally
        {
            _applyGate.Release();
        }
    }

    // The checkbox is put back to what the registry says when the entry cannot be written, or it
    // would show a start-up setting that is not in effect.
    private bool TryApplyAutoStart()
    {
        try
        {
            if (AutoStart)
            {
                _autoStartService.Enable();
            }
            else
            {
                _autoStartService.Disable();
            }

            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            _logger?.LogWarning(ex, "Could not change the auto-start entry.");
            _suppressApply = true;
            try
            {
                AutoStart = _autoStartService.IsEnabled;
            }
            finally
            {
                _suppressApply = false;
            }

            _confirmation.Notify("Not changed", $"The Windows start-up entry could not be changed:\n{ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Starts the uninstaller of this copy — the installer's, or for a portable copy the Manager
    /// itself with <c>--uninstall</c>. It asks for confirmation itself; this Manager then has to
    /// exit so that its own files can be deleted.
    /// </summary>
    [RelayCommand]
    private void Uninstall()
    {
        var uninstaller = _install.FindUninstaller();
        if (uninstaller is null && !_install.IsPortable)
        {
            _confirmation.Notify(
                "Uninstall",
                "No uninstaller was found next to this copy of TaskbarFolders. A development build has none; " +
                $"run TaskbarFolders.Manager.exe {UninstallArguments.Uninstall} to clean up instead.");
            return;
        }

        var psi = uninstaller is not null
            ? new ProcessStartInfo(uninstaller) { UseShellExecute = true }
            : new ProcessStartInfo(_install.PortableManagerPath, UninstallArguments.Uninstall) { UseShellExecute = false };
        uninstaller ??= _install.PortableManagerPath;

        if (!_processes.TryStart(psi))
        {
            _logger?.LogWarning("Uninstall: could not start {Path}.", uninstaller);
            _confirmation.Notify("Uninstall", $"The uninstaller could not be started:\n{uninstaller}");
            return;
        }

        UninstallStarted?.Invoke(this, EventArgs.Empty);
    }

    partial void OnThemeChanged(ThemePreference value) => QueueApply(nameof(Theme));
    partial void OnAutoStartChanged(bool value) => QueueApply(nameof(AutoStart));
    partial void OnEnableAnimationsChanged(bool value) => QueueApply(nameof(EnableAnimations));
    partial void OnShowInExplorerMenuChanged(bool value) => QueueApply(nameof(ShowInExplorerMenu));
    partial void OnPopupPositionChanged(PopupPositionPreference value) => QueueApply(nameof(PopupPosition));
    partial void OnPopupBackdropChanged(PopupBackdrop value) => QueueApply(nameof(PopupBackdrop));

    private void QueueApply(string setting)
    {
        if (!_suppressApply)
        {
            _ = ApplyAsync(setting);
        }
    }
}
