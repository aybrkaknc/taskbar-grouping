using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace TaskbarFolders.Core.Interop;

/// <summary>
/// Material to apply behind a window via <c>DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE)</c>.
/// Supported on Windows 11 22H2+; older versions silently keep the classic chrome.
/// </summary>
public enum WindowBackdropKind
{
    /// <summary>Let the system decide.</summary>
    Auto = 0,

    /// <summary>No backdrop material.</summary>
    None = 1,

    /// <summary>Mica — desktop-tinted material for primary app windows.</summary>
    Mica = 2,

    /// <summary>Acrylic — translucent material best suited for transient surfaces (popups, flyouts).</summary>
    Acrylic = 3,

    /// <summary>Mica Alt — slightly more vibrant Mica used by tabbed surfaces in Win11 23H2+.</summary>
    MicaAlt = 4,
}

/// <summary>
/// Thin wrapper around <c>DwmSetWindowAttribute</c> for applying Mica / Acrylic backdrop
/// material. Reused by both Manager (Mica on main window) and Launcher (Acrylic on popup);
/// previously each project had its own duplicate P/Invoke.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowBackdrop
{
    private const uint DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const uint DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    /// <summary>First build that honours <c>DWMWA_SYSTEMBACKDROP_TYPE</c> (Windows 11 22H2).</summary>
    private const int SystemBackdropMinimumBuild = 22621;

    /// <summary>
    /// Whether this Windows version honours <see cref="TryApply"/>. Callers that must choose a
    /// window mode before the HWND exists — a system backdrop never paints behind a layered
    /// (<c>AllowsTransparency</c>) window — decide on this rather than on a failed call.
    /// </summary>
    public static bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, SystemBackdropMinimumBuild);

    /// <summary>
    /// Attempts to apply the specified backdrop material to the supplied window handle.
    /// </summary>
    /// <param name="hwnd">Target window handle (non-zero).</param>
    /// <param name="kind">Backdrop material to apply.</param>
    /// <returns>
    /// <see langword="true"/> if the call succeeded (pre-22H2 Windows return non-zero HRESULTs;
    /// in that case <see langword="false"/> is returned and the caller's themed fallback brush
    /// stays as-is).
    /// </returns>
    public static bool TryApply(IntPtr hwnd, WindowBackdropKind kind)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        var value = (int)kind;
        var hr = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref value, sizeof(int));
        return hr == 0;
    }

    /// <summary>
    /// Switches the window's non-client area (title bar and border) between the light and
    /// dark system caption.
    /// </summary>
    /// <param name="hwnd">Target window handle (non-zero).</param>
    /// <param name="enabled"><see langword="true"/> for the dark caption.</param>
    /// <returns><see langword="true"/> if the call succeeded.</returns>
    /// <remarks>
    /// Without this the caption stays light while the client area is dark, which is what a
    /// Mica window looked like before v0.4.7 — the backdrop only shows through the
    /// non-client strip, so the mismatch is plainly visible. Windows 10 builds before 20H1
    /// do not know the attribute and return a non-zero HRESULT; the caller ignores it and
    /// the caption simply stays light.
    /// </remarks>
    public static bool TrySetDarkTitleBar(IntPtr hwnd, bool enabled)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        var value = enabled ? 1 : 0;
        var hr = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
        return hr == 0;
    }

    /// <summary>Asks DWM to round the window's corners, as Windows 11 does for its own flyouts.</summary>
    /// <param name="hwnd">Target window handle (non-zero).</param>
    /// <returns><see langword="true"/> if the call succeeded; Windows 10 does not know the attribute.</returns>
    public static bool TrySetRoundedCorners(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        var value = DWMWCP_ROUND;
        var hr = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref value, sizeof(int));
        return hr == 0;
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hWnd, uint attr, ref int attrValue, int attrSize);
}
