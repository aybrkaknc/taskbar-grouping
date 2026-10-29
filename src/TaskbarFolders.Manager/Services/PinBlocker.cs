namespace TaskbarFolders.Manager.Services;

/// <summary>Why Windows will not take a pin request from this app, as far as the machine tells.</summary>
public enum PinBlocker
{
    /// <summary>Nothing readable explains it; the generic message and the manual way apply.</summary>
    Unknown = 0,

    /// <summary>System notifications are off; Windows shows the pin request as a notification.</summary>
    NotificationsOff,

    /// <summary>This Windows build wants a Limited Access Feature key from Microsoft for taskbar pinning.</summary>
    TokenRequired,

    /// <summary>A policy turns off pinning to the taskbar.</summary>
    PolicyBlocked,
}

/// <summary>The rule that turns what the registry says into a <see cref="PinBlocker"/>. Pure.</summary>
public static class PinBlockerRules
{
    /// <summary>
    /// Picks the cause to report. A policy or a missing key cannot be fixed by the user, so they
    /// outrank notifications, which the user can turn on.
    /// </summary>
    /// <param name="noPinningPolicy">Largest <c>NoPinningToTaskbar</c> value found under HKCU/HKLM policies, or <see langword="null"/>.</param>
    /// <param name="lafSeedValue">The taskbar-pin Limited Access Feature value, or <see langword="null"/> when absent.</param>
    /// <param name="toastEnabled">HKCU <c>PushNotifications\ToastEnabled</c>, or <see langword="null"/> when absent.</param>
    public static PinBlocker Classify(int? noPinningPolicy, int? lafSeedValue, int? toastEnabled)
    {
        if (noPinningPolicy is > 0)
        {
            return PinBlocker.PolicyBlocked;
        }

        if (lafSeedValue is > 0)
        {
            return PinBlocker.TokenRequired;
        }

        return toastEnabled == 0 ? PinBlocker.NotificationsOff : PinBlocker.Unknown;
    }
}
