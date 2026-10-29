namespace TaskbarFolders.Manager.Services;

/// <summary>
/// What the Manager does around a pin request that Windows will not take: finding out why,
/// sending the user to the setting that fixes it, and showing the shortcut to pin by hand.
/// </summary>
public interface IPinHelper
{
    /// <summary>Reads the machine's state to say why pinning is unavailable. Never throws.</summary>
    PinBlocker Diagnose();

    /// <summary>Opens Windows' notification settings.</summary>
    void OpenNotificationSettings();

    /// <summary>Opens Explorer with <paramref name="path"/> selected.</summary>
    /// <param name="path">Full path of a file.</param>
    void RevealInExplorer(string path);
}
