using System;
using System.Runtime.InteropServices;
using System.Text;

namespace DisplayRest;

internal static partial class WindowsNative
{
    internal const int KeyboardLowLevel = 13;
    internal const int MouseLowLevel = 14;
    internal const int PowerBroadcast = 0x0218;
    internal const int PowerSettingChange = 0x8013;
    internal const int KeyDown = 0x0100;
    internal const int SystemKeyDown = 0x0104;
    internal const int MouseMove = 0x0200;
    internal const int LeftButtonDown = 0x0201;
    internal const int RightButtonDown = 0x0204;
    internal const int MiddleButtonDown = 0x0207;
    internal const int MouseWheel = 0x020A;
    internal const int XButtonDown = 0x020B;
    internal const int HorizontalWheel = 0x020E;
    internal static readonly Guid SessionDisplayStatus = new("2B84C20E-AD23-4DDF-93DB-05FFBD7EFCA5");

    internal delegate nint InputHook(int code, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    internal static extern nint SetWindowsHookEx(int type, InputHook callback, nint module, uint thread);

    [DllImport("user32.dll")]
    internal static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    internal static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW")]
    internal static extern nint GetModuleHandle(nint name);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
    internal static extern nint DefWindowProc(nint window, int message, nint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);

    internal static void SetDisplays(nint window, bool on)
    {
        if (on)
            SetThreadExecutionState(2); // ES_DISPLAY_REQUIRED, once

        DefWindowProc(window, 0x0112, 0xF170, on ? -1 : 2); // WM_SYSCOMMAND, SC_MONITORPOWER
    }

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint RegisterPowerSettingNotification(nint window, in Guid setting, uint flags);

    [DllImport("user32.dll")]
    internal static extern bool UnregisterPowerSettingNotification(nint registration);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(nint window, uint attribute, in int value, int size);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    internal static extern uint MapVirtualKey(uint key, uint mapType);

    [DllImport("user32.dll", EntryPoint = "GetKeyNameTextW", CharSet = CharSet.Unicode)]
    internal static extern int GetKeyNameText(int lParam, StringBuilder text, int capacity);
}
