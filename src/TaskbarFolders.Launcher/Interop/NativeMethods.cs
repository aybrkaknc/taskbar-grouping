using System;
using System.Runtime.InteropServices;

namespace TaskbarFolders.Launcher.Interop;

/// <summary>
/// P/Invoke entry points used by the popup positioning helper. Internal to satisfy CA1401
/// (do not expose P/Invoke methods on public types).
/// </summary>
internal static class NativeMethods
{
    // shell32
    [DllImport("shell32.dll", ExactSpelling = true)]
    public static extern IntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    public const uint ABM_GETTASKBARPOS = 0x5;

    public const uint ABE_LEFT = 0;
    public const uint ABE_TOP = 1;
    public const uint ABE_RIGHT = 2;
    public const uint ABE_BOTTOM = 3;

    // Sets the AppUserModelID for the current process. Must be called before any window is
    // shown so taskbar pinning identity locks to the expected tile.
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SetCurrentProcessExplicitAppUserModelID(
        [MarshalAs(UnmanagedType.LPWStr)] string appId);

    // Reads back the AUMID Windows assigned to the current process. Returns 0 (S_OK) if set
    // and writes a CoTaskMem-allocated PWSTR to ppAppID (caller frees). Non-zero HRESULT
    // means no explicit AUMID is set; ppAppID is null. Used by the v0.4 AUMID-recovery
    // fallback when --group-id is absent (e.g. Windows launches the pinned-via-API tile
    // without preserving the original command line).
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetCurrentProcessExplicitAppUserModelID(out IntPtr ppAppID);

    /// <summary>
    /// Convenience wrapper around <see cref="GetCurrentProcessExplicitAppUserModelID"/> that
    /// marshals the returned PWSTR to a managed string and frees the CoTaskMem allocation.
    /// </summary>
    public static string? TryGetCurrentProcessAumid()
    {
        var hr = GetCurrentProcessExplicitAppUserModelID(out var ptr);
        if (hr != 0 || ptr == IntPtr.Zero)
        {
            return null;
        }
        try
        {
            return Marshal.PtrToStringUni(ptr);
        }
        finally
        {
            Marshal.FreeCoTaskMem(ptr);
        }
    }

    // user32
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    public const uint MONITOR_DEFAULTTONEAREST = 0x2;

    // Effective DPI of a monitor (per-monitor scaling). Win 8.1+.
    [DllImport("shcore.dll", ExactSpelling = true)]
    public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    public const int MDT_EFFECTIVE_DPI = 0;

    // System DPI — used only for the GetCursorPos-failure fallback anchor where no
    // monitor handle is available yet. Win10 1607+.
    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern uint GetDpiForSystem();

    // shcore — per-monitor DPI awareness
    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = (HANDLE)-4
    public static readonly IntPtr DpiAwarenessContextPerMonitorAwareV2 = new(-4);

    // Low-level mouse hook — lets the popup see clicks outside itself even when it never got
    // activated, which Deactivated cannot. The callback runs on the installing thread's
    // message loop, so it must return quickly.
    public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    public const int WH_MOUSE_LL = 14;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_RBUTTONDOWN = 0x0204;
    public const int WM_MBUTTONDOWN = 0x0207;
    public const int WM_XBUTTONDOWN = 0x020B;

    // DwmSetWindowAttribute backdrop control moved to TaskbarFolders.Core.Interop.WindowBackdrop
    // — both Manager and Launcher consume that single helper.
}
