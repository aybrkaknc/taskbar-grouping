using System.Collections.Generic;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace TaskbarFolders.Manager.Services;

/// <summary>Default <see cref="IRegistryWriter"/>, on <see cref="Registry.CurrentUser"/>.</summary>
[SupportedOSPlatform("windows")]
public sealed class CurrentUserRegistryWriter : IRegistryWriter
{
    /// <inheritdoc/>
    public void DeleteTree(string subKey) =>
        Registry.CurrentUser.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);

    /// <inheritdoc/>
    public void SetString(string subKey, string? name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(subKey, writable: true);
        key.SetValue(name ?? string.Empty, value, RegistryValueKind.String);
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> GetValueNames(string subKey)
    {
        using var key = Registry.CurrentUser.OpenSubKey(subKey, writable: false);
        return key?.GetValueNames() ?? [];
    }

    /// <inheritdoc/>
    public void DeleteValue(string subKey, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(subKey, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}
