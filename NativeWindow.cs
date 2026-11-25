using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using static DisplayRest.WindowsNative;

namespace DisplayRest;

internal sealed class NativeWindow : IDisposable
{
    internal delegate nint? Handler(nint window, int message, nint wParam, nint lParam);
    private readonly WindowProcedure _callback;
    private readonly string _name;
    internal nint Handle { get; private set; }
    internal static readonly nint Module = GetModuleHandle(0);
    internal static readonly nint Icon = ExtractIcon(Module, Environment.ProcessPath!, 0);

    internal NativeWindow(string name, Handler handler, string title = "", uint style = 0x80000000,
        int x = 0, int y = 0, int width = 1, int height = 1, uint extendedStyle = 0)
    {
        _name = name;
        _callback = (hwnd, message, w, l) => handler(hwnd, message, w, l) ?? DefWindowProc(hwnd, message, w, l);
        var windowClass = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            Style = 8, // CS_DBLCLKS
            Procedure = Marshal.GetFunctionPointerForDelegate(_callback),
            Instance = Module,
            Icon = Icon,
            SmallIcon = Icon,
            Cursor = LoadCursor(0, 32512),
            Name = name
        };
        if (RegisterClass(in windowClass) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        Handle = CreateWindow(extendedStyle, name, title, style, x, y, width, height, 0, 0, Module, 0);
        if (Handle != 0)
            return;
        int error = Marshal.GetLastWin32Error();
        UnregisterClass(name, Module);
        throw new Win32Exception(error);
    }

    internal static void Pump(Func<nint>? dialog = null, Func<Message, bool>? filter = null)
    {
        int result;
        while ((result = GetMessage(out var message, 0, 0, 0)) > 0)
        {
            if (filter?.Invoke(message) == true)
                continue;
            var hwnd = dialog?.Invoke() ?? 0;
            if (hwnd != 0 && IsDialogMessage(hwnd, ref message))
                continue;
            TranslateMessage(in message);
            DispatchMessage(in message);
        }
        if (result < 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public void Dispose()
    {
        if (Handle == 0)
            return;
        DestroyWindow(Handle);
        Handle = 0;
        UnregisterClass(_name, Module);
        GC.KeepAlive(_callback);
    }
}

internal static partial class WindowsNative
{
    internal delegate nint WindowProcedure(nint window, int message, nint wParam, nint lParam);
    internal const int AppMessage = 0x8000;
    internal const int TimerMessage = 0x0113;
    internal const int CloseMessage = 0x0010;
    internal const int DestroyMessage = 0x0002;
    internal const int CommandMessage = 0x0111;
    internal const uint Visible = 0x10000000;
    internal const uint Child = 0x40000000;
    internal const uint TabStop = 0x00010000;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point { internal int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect { internal int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Message
    {
        internal nint Window;
        internal uint Id;
        internal nint WParam, LParam;
        internal uint Time;
        internal Point Point;
        internal uint Private;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WindowClass
    {
        internal uint Size;
        internal uint Style;
        internal nint Procedure;
        internal int ClassExtra, WindowExtra;
        internal nint Instance, Icon, Cursor, Background;
        internal string? Menu, Name;
        internal nint SmallIcon;
    }

    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern ushort RegisterClass(in WindowClass windowClass);

    [DllImport("user32.dll", EntryPoint = "UnregisterClassW", CharSet = CharSet.Unicode)]
    internal static extern bool UnregisterClass(string name, nint instance);
    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern nint CreateWindow(uint extendedStyle, string name, string? title, uint style,
        int x, int y, int width, int height, nint parent, nint id, nint instance, nint parameter);
    [DllImport("user32.dll")]
    internal static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)]
    internal static extern int GetMessage(out Message message, nint window, uint min, uint max);
    [DllImport("user32.dll")]
    internal static extern bool TranslateMessage(in Message message);
    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
    internal static extern nint DispatchMessage(in Message message);
    [DllImport("user32.dll", EntryPoint = "IsDialogMessageW")]
    internal static extern bool IsDialogMessage(nint window, ref Message message);
    [DllImport("user32.dll")]
    internal static extern void PostQuitMessage(int exitCode);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    internal static extern bool PostMessage(nint window, int message, nint w = 0, nint l = 0);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    internal static extern nint SendMessage(nint window, int message, nint w = 0, nint l = 0);
    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
    internal static extern nint SendText(nint window, int message, nint w, string text);
    [DllImport("user32.dll")]
    internal static extern nuint SetTimer(nint window, nuint id, uint milliseconds, nint callback = 0);
    [DllImport("user32.dll")]
    internal static extern bool KillTimer(nint window, nuint id);
    [DllImport("shell32.dll", EntryPoint = "ExtractIconW", CharSet = CharSet.Unicode)]
    internal static extern nint ExtractIcon(nint instance, string path, uint index);
    [DllImport("user32.dll", EntryPoint = "LoadCursorW")]
    internal static extern nint LoadCursor(nint instance, nint cursor);
    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    internal static extern int MessageBox(nint window, string text, string caption, uint type);
}
