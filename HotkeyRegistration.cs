using System.Runtime.InteropServices;

namespace DisplayRest;

internal enum HotkeyCheck { Available, InUse, Unavailable, Gamepad }

internal static class HotkeyRegistration
{
    private const int ProbeId = 1;
    private const int AlreadyRegistered = 1409;

    internal static HotkeyCheck Check(nint window, Shortcut shortcut)
    {
        if (shortcut.IsGamepad)
            return HotkeyCheck.Gamepad;

        // RegisterHotKey expects a main key and reserves F12 for the debugger.
        if (shortcut.Key is 0 or 0x7B)
            return HotkeyCheck.Unavailable;

        uint flags = 0;
        if (shortcut.Modifiers.HasFlag(Modifiers.Alt)) flags |= 0x0001;
        if (shortcut.Modifiers.HasFlag(Modifiers.Ctrl)) flags |= 0x0002;
        if (shortcut.Modifiers.HasFlag(Modifiers.Shift)) flags |= 0x0004;
        if (shortcut.Modifiers.HasFlag(Modifiers.Win)) flags |= 0x0008;

        var status = Probe(window, flags, (uint)shortcut.Key);
        if (shortcut.Key is not (Shortcut.Pause or Shortcut.ScrollLock) ||
            !shortcut.Modifiers.HasFlag(Modifiers.Ctrl) || status == HotkeyCheck.InUse)
            return status;

        // RegisterHotKey is virtual-key based and cannot distinguish the two VK_CANCEL inputs.
        var breakStatus = Probe(window, flags, Shortcut.ControlBreak);
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
