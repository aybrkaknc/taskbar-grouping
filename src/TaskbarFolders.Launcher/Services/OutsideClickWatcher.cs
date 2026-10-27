using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using TaskbarFolders.Launcher.Interop;

namespace TaskbarFolders.Launcher.Services;

/// <summary>
/// Reports a mouse-button press anywhere on the desktop that lands outside a given window,
/// through a process-local low-level mouse hook.
/// </summary>
/// <remarks>
/// The popup used to rely on <c>Deactivated</c> alone, which can only fire after the window
/// has been activated once. When Windows refuses the popup the foreground — a foreground lock,
/// or a spawn the shell did not attribute to the click — it never deactivates, and a click
/// elsewhere did nothing until the three-second fallback. The hook sees the click either way.
/// The callback runs on the installing thread's message loop, so it stays cheap and always
/// passes the event on.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class OutsideClickWatcher : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly Action _onOutsideClick;

    // Held in a field: the hook keeps only a native pointer to this delegate, so a delegate
    // the GC could collect would crash the process on the next mouse event.
    private readonly NativeMethods.LowLevelMouseProc _proc;
    private IntPtr _hook;

    /// <summary>Installs the hook for <paramref name="hwnd"/>.</summary>
    /// <param name="hwnd">Window whose rectangle counts as "inside".</param>
    /// <param name="onOutsideClick">Called on the installing thread for each press outside the window.</param>
    public OutsideClickWatcher(IntPtr hwnd, Action onOutsideClick)
    {
        ArgumentNullException.ThrowIfNull(onOutsideClick);

        _hwnd = hwnd;
        _onOutsideClick = onOutsideClick;
        _proc = HookCallback;
        _hook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL, _proc, NativeMethods.GetModuleHandle(null), 0);
    }

    /// <summary>Whether Windows accepted the hook. When it did not, <c>Deactivated</c> is the only trigger left.</summary>
    public bool IsInstalled => _hook != IntPtr.Zero;

    /// <summary>
    /// Whether the point lies outside the rectangle. Right and bottom edges are exclusive, as
    /// in Win32 <c>RECT</c>. All values are device pixels — the hook and
    /// <c>GetWindowRect</c> both report physical coordinates in a per-monitor-aware process.
    /// </summary>
    /// <param name="x">Point X.</param>
    /// <param name="y">Point Y.</param>
    /// <param name="left">Rectangle left edge (inclusive).</param>
    /// <param name="top">Rectangle top edge (inclusive).</param>
    /// <param name="right">Rectangle right edge (exclusive).</param>
    /// <param name="bottom">Rectangle bottom edge (exclusive).</param>
    /// <returns><see langword="true"/> when the point is outside.</returns>
    public static bool IsOutside(int x, int y, int left, int top, int right, int bottom) =>
        x < left || x >= right || y < top || y >= bottom;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            _ = NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsButtonDown(wParam.ToInt64()) && NativeMethods.GetWindowRect(_hwnd, out var rect))
        {
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            if (IsOutside(info.pt.X, info.pt.Y, rect.Left, rect.Top, rect.Right, rect.Bottom))
            {
                _onOutsideClick();
            }
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static bool IsButtonDown(long message) => message is
        NativeMethods.WM_LBUTTONDOWN or
        NativeMethods.WM_RBUTTONDOWN or
        NativeMethods.WM_MBUTTONDOWN or
        NativeMethods.WM_XBUTTONDOWN;
}
