using System.Runtime.InteropServices;

namespace DisplayRest;

internal static partial class WindowsNative
{
    // Use NOTIFYICONDATA through dwInfoFlags without optional GUID and balloon icon fields.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct NotifyIconData
    {
        internal uint Size;
        internal nint Window;
        internal uint Id, Flags, Callback;
        internal nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        internal string Tip;
        internal uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        internal string? Info;
        internal uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        internal string? InfoTitle;
        internal uint InfoFlags;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    internal static extern bool ShellNotifyIcon(uint action, ref NotifyIconData data);
    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode)]
    internal static extern int RegisterWindowMessage(string message);
    [DllImport("user32.dll")]
    internal static extern nint CreatePopupMenu();
    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode)]
    internal static extern bool AppendMenu(nint menu, uint flags, nuint id, string? title);
    [DllImport("user32.dll")]
    internal static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")]
    internal static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint owner, nint rect);
    [DllImport("user32.dll")]
    internal static extern bool GetCursorPos(out Point point);
}
