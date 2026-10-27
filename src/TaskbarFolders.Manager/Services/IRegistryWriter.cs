namespace TaskbarFolders.Manager.Services;

/// <summary>
/// The few operations the Explorer menu needs on <c>HKEY_CURRENT_USER</c>. Abstracted so the
/// key layout can be tested without touching the real registry.
/// </summary>
public interface IRegistryWriter
{
    /// <summary>Deletes <paramref name="subKey"/> and everything under it; silent when it does not exist.</summary>
    /// <param name="subKey">Path under <c>HKEY_CURRENT_USER</c>.</param>
    void DeleteTree(string subKey);

    /// <summary>Creates <paramref name="subKey"/> if needed and sets a string value on it.</summary>
    /// <param name="subKey">Path under <c>HKEY_CURRENT_USER</c>.</param>
    /// <param name="name">Value name; <see langword="null"/> for the key's default value.</param>
    /// <param name="value">String data.</param>
    void SetString(string subKey, string? name, string value);
}
