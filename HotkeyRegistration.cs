using System.Runtime.InteropServices;

namespace DisplayRest;

internal enum HotkeyCheck { Available, InUse, Unavailable }

internal static class HotkeyRegistration
{
    private const int ProbeId = 1;
    private const int AlreadyRegistered = 1409;

    internal static HotkeyCheck Check(nint window, Shortcut shortcut)
    {
        // RegisterHotKey expects a main key and reserves F12 for the debugger.
        if (shortcut.Key is 0 or 0x7B)
            return HotkeyCheck.Unavailable;

        uint flags = 0;
        if (shortcut.Modifiers.HasFlag(Modifiers.Alt)) flags |= 0x0001;
        if (shortcut.Modifiers.HasFlag(Modifiers.Ctrl)) flags |= 0x0002;
        if (shortcut.Modifiers.HasFlag(Modifiers.Shift)) flags |= 0x0004;
        if (shortcut.Modifiers.HasFlag(Modifiers.Win)) flags |= 0x0008;

        var status = Probe(window, flags, (uint)shortcut.Key);
        if (shortcut.Key != Shortcut.Pause || !shortcut.Modifiers.HasFlag(Modifiers.Ctrl) || status == HotkeyCheck.InUse)
            return status;

        var breakStatus = Probe(window, flags, 0x03); // Ctrl+Pause may arrive as VK_CANCEL.
        return breakStatus == HotkeyCheck.InUse ? breakStatus :
            status == HotkeyCheck.Available && breakStatus == HotkeyCheck.Available ? status : HotkeyCheck.Unavailable;
    }

    private static HotkeyCheck Probe(nint window, uint flags, uint key)
    {
        if (!RegisterHotKey(window, ProbeId, flags, key))
            return Marshal.GetLastWin32Error() == AlreadyRegistered ? HotkeyCheck.InUse : HotkeyCheck.Unavailable;
        return UnregisterHotKey(window, ProbeId) ? HotkeyCheck.Available : HotkeyCheck.Unavailable;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(nint window, int id);
}
