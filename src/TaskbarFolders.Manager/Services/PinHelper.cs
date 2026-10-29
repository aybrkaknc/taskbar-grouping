using System;
using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace TaskbarFolders.Manager.Services;

/// <summary>Default <see cref="IPinHelper"/>: the registry for the diagnosis, <see cref="IProcessRunner"/> for the rest.</summary>
[SupportedOSPlatform("windows")]
public sealed class PinHelper : IPinHelper
{
    /// <summary>Settings page for notifications.</summary>
    public const string NotificationSettingsUri = "ms-settings:notifications";

    // The Limited Access Feature of taskbar pinning, and the value under it that says whether a
    // token is still required (Microsoft's "Pin your app to the taskbar").
    private const string LafKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModel\LimitedAccessFeatures\com.microsoft.windows.taskbar.pin";
    private const string LafSeed = "4096B239A7295B635C090E647E867B5707DA6AB6CB78340B01FE4E0C8F4953D4";
    private const string PoliciesKey = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";
    private const string NotificationsKey = @"Software\Microsoft\Windows\CurrentVersion\PushNotifications";

    private readonly IProcessRunner _processes;
    private readonly ILogger<PinHelper>? _logger;

    /// <summary>Initializes a new instance.</summary>
    public PinHelper(IProcessRunner processes, ILogger<PinHelper>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(processes);

        _processes = processes;
        _logger = logger;
    }

    /// <inheritdoc/>
    public PinBlocker Diagnose()
    {
        try
        {
            var policy = Max(
                ReadDword(RegistryHive.CurrentUser, PoliciesKey, "NoPinningToTaskbar"),
                ReadDword(RegistryHive.LocalMachine, PoliciesKey, "NoPinningToTaskbar"));
            var laf = ReadDword(RegistryHive.LocalMachine, LafKey, LafSeed);
            var toast = ReadDword(RegistryHive.CurrentUser, NotificationsKey, "ToastEnabled");

            var blocker = PinBlockerRules.Classify(policy, laf, toast);
            _logger?.LogInformation(
                "Pin blocker {Blocker} (NoPinningToTaskbar={Policy}, LAF={Laf}, ToastEnabled={Toast}).",
                blocker, policy, laf, toast);
            return blocker;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            _logger?.LogWarning(ex, "Could not read the pin settings from the registry.");
            return PinBlocker.Unknown;
        }
    }

    /// <inheritdoc/>
    public void OpenNotificationSettings()
    {
        if (!_processes.TryStart(new ProcessStartInfo(NotificationSettingsUri) { UseShellExecute = true }))
        {
            _logger?.LogWarning("Could not open {Uri}.", NotificationSettingsUri);
        }
    }

    /// <inheritdoc/>
    public void RevealInExplorer(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var started = _processes.TryStart(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{path}\"",
            UseShellExecute = true,
        });
        if (!started)
        {
            _logger?.LogWarning("Could not open Explorer on {Path}.", path);
        }
    }

    private static int? Max(int? a, int? b) =>
        a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);

    // 64-bit view on purpose: the Manager is x64 only, and the keys are Windows', not its own.
    private static int? ReadDword(RegistryHive hive, string subKey, string name)
    {
        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = root.OpenSubKey(subKey, writable: false);
        return key?.GetValue(name) is int value ? value : null;
    }
}
