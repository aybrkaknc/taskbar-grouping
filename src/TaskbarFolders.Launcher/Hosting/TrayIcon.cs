using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Interop;
using TaskbarFolders.Launcher.Interop;

namespace TaskbarFolders.Launcher.Hosting;

/// <summary>
/// The resident launcher's notification-area icon: the only sign that it is running, and the way
/// to open the Manager or quit it. Plain Win32 (<c>Shell_NotifyIcon</c> on a message-only window)
/// rather than WinForms' <c>NotifyIcon</c>, so the launcher does not load a second UI framework
/// into a process that stays in memory. Re-adds the icon when Explorer restarts.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class TrayIcon : IDisposable
{
    private const int WmApp = 0x8000;
    private const int TrayCallback = WmApp + 1;
    private const int WmLButtonUp = 0x0202;
    private const int WmRButtonUp = 0x0205;
    private const int WmNull = 0x0000;

    private const uint NimAdd = 0;
    private const uint NimDelete = 2;
    private const uint NifMessage = 0x1;
    private const uint NifIcon = 0x2;
    private const uint NifTip = 0x4;

    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCmd = 0x0100;
    private const uint MfString = 0x0000;

    private const int CommandOpen = 1;
    private const int CommandExit = 2;

    private static readonly IntPtr _messageOnlyParent = new(-3);

    private readonly Action _openManager;
    private readonly Action _exit;
    private readonly string _tooltip;
    private readonly uint _taskbarCreated;
    private HwndSource? _source;
    private IntPtr _icon;
    private bool _added;

    /// <summary>Initializes a new instance and shows the icon.</summary>
    /// <param name="tooltip">Text shown when the pointer rests on the icon.</param>
    /// <param name="openManager">Called for a left click or the "Open" menu item.</param>
    /// <param name="exit">Called for the "Exit" menu item.</param>
    public TrayIcon(string tooltip, Action openManager, Action exit)
    {
        ArgumentException.ThrowIfNullOrEmpty(tooltip);
        ArgumentNullException.ThrowIfNull(openManager);
        ArgumentNullException.ThrowIfNull(exit);

        _tooltip = tooltip;
        _openManager = openManager;
        _exit = exit;
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");

        var parameters = new HwndSourceParameters("TaskbarFoldersTray")
        {
            ParentWindow = _messageOnlyParent,
            WindowStyle = 0,
            Width = 0,
            Height = 0,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        _icon = LoadOwnIcon();
        Add();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_added && _source is not null)
        {
            var data = NewData(_source.Handle);
            _ = Shell_NotifyIcon(NimDelete, ref data);
            _added = false;
        }

        _source?.Dispose();
        _source = null;

        if (_icon != IntPtr.Zero)
        {
            _ = DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }
    }

    private static IntPtr LoadOwnIcon()
    {
        var path = Environment.ProcessPath;
        if (path is null)
        {
            return IntPtr.Zero;
        }

        var small = IntPtr.Zero;
        var large = IntPtr.Zero;
        _ = ExtractIconEx(path, 0, ref large, ref small, 1);
        if (large != IntPtr.Zero)
        {
            _ = DestroyIcon(large);
        }

        return small;
    }

    private void Add()
    {
        if (_source is null)
        {
            return;
        }

        var data = NewData(_source.Handle);
        data.uFlags = NifMessage | NifIcon | NifTip;
        data.uCallbackMessage = TrayCallback;
        data.hIcon = _icon;
        data.szTip = _tooltip;
        _added = Shell_NotifyIcon(NimAdd, ref data);
    }

    private static NotifyIconData NewData(IntPtr hwnd) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
        hWnd = hwnd,
        uID = 1,
        szTip = string.Empty,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_taskbarCreated != 0 && msg == _taskbarCreated)
        {
            // Explorer restarted and lost every icon: add ours again.
            Add();
            handled = true;
            return IntPtr.Zero;
        }

        if (msg != TrayCallback)
        {
            return IntPtr.Zero;
        }

        handled = true;
        switch ((int)(lParam.ToInt64() & 0xFFFF))
        {
            case WmLButtonUp:
                _openManager();
                break;
            case WmRButtonUp:
                ShowMenu(hwnd);
                break;
        }

        return IntPtr.Zero;
    }

    private void ShowMenu(IntPtr hwnd)
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            _ = AppendMenu(menu, MfString, CommandOpen, "Open Taskbar Folders");
            _ = AppendMenu(menu, MfString, CommandExit, "Exit");

            // Without the foreground, the menu would not close when the user clicks elsewhere.
            _ = SetForegroundWindow(hwnd);
            _ = NativeMethods.GetCursorPos(out var at);
            var command = TrackPopupMenuEx(menu, TpmRightButton | TpmReturnCmd, at.X, at.Y, hwnd, IntPtr.Zero);
            _ = PostMessage(hwnd, WmNull, IntPtr.Zero, IntPtr.Zero);

            switch (command)
            {
                case CommandOpen:
                    _openManager();
                    break;
                case CommandExit:
                    _exit();
                    break;
            }
        }
        finally
        {
            _ = DestroyMenu(menu);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public uint dwState;
        public uint dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public uint uVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("shell32.dll", EntryPoint = "ExtractIconExW", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, ref IntPtr large, ref IntPtr small, uint count);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, nuint id, string text);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr parameters);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
}
