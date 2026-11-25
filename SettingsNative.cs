using System.Runtime.InteropServices;
using System.Text;

namespace DisplayRest;

internal static partial class WindowsNative
{
    internal const int PaintMessage = 0x000F;
    internal const int SystemCommand = 0x0112;
    internal const int ActivateMessage = 0x0006;
    internal const int DrawItemMessage = 0x002B;
    internal const int ColorEditMessage = 0x0133;
    internal const int ColorListMessage = 0x0134;
    internal const int ColorButtonMessage = 0x0135;
    internal const int ColorStaticMessage = 0x0138;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Paint
    {
        internal nint Dc;
        internal int Erase;
        internal Rect Area;
        internal int Restore, IncUpdate;
        private unsafe fixed byte Reserved[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DrawItem
    {
        internal uint Type, Id, ItemId, Action, State;
        internal nint Window, Dc;
        internal Rect Area;
        internal nuint Data;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct ToolInfo
    {
        internal uint Size, Flags;
        internal nint Window, Id;
        internal Rect Area;
        internal nint Instance, Text, Data, Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CommonControls
    {
        internal uint Size, Flags;
    }

    [DllImport("comctl32.dll")]
    internal static extern bool InitCommonControlsEx(in CommonControls controls);
    [DllImport("user32.dll")]
    internal static extern uint GetDpiForSystem();
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool AdjustWindowRectEx(ref Rect bounds, uint style, bool menu, uint extendedStyle);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    internal static extern bool GetWorkArea(uint action, uint param, out Rect area, uint flags);
    [DllImport("user32.dll", EntryPoint = "SetWindowTextW", CharSet = CharSet.Unicode)]
    internal static extern bool SetWindowText(nint window, string text);
    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(nint window, StringBuilder text, int capacity);
    [DllImport("user32.dll")]
    internal static extern bool EnableWindow(nint window, bool enabled);
    [DllImport("user32.dll")]
    internal static extern bool InvalidateRect(nint window, nint rect, bool erase);
    [DllImport("user32.dll")]
    internal static extern nint BeginPaint(nint window, out Paint paint);
    [DllImport("user32.dll")]
    internal static extern bool EndPaint(nint window, in Paint paint);
    [DllImport("user32.dll")]
    internal static extern int FillRect(nint dc, in Rect rect, nint brush);
    [DllImport("user32.dll")]
    internal static extern bool DrawIconEx(nint dc, int x, int y, nint icon, int w, int h, uint step, nint brush, uint flags);
    [DllImport("user32.dll", EntryPoint = "DrawTextW", CharSet = CharSet.Unicode)]
    internal static extern int DrawText(nint dc, string text, int count, ref Rect area, uint align);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    internal static extern nint SendTool(nint window, int message, nint wParam, in ToolInfo tool);
    [DllImport("user32.dll")]
    internal static extern bool GetClientRect(nint window, out Rect rect);
    [DllImport("gdi32.dll")]
    internal static extern nint CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")]
    internal static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")]
    internal static extern int SaveDC(nint dc);
    [DllImport("gdi32.dll")]
    internal static extern bool RestoreDC(nint dc, int saved);
    [DllImport("gdi32.dll")]
    internal static extern nint GetStockObject(int obj);
    [DllImport("gdi32.dll")]
    internal static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")]
    internal static extern uint SetTextColor(nint dc, uint color);
    [DllImport("gdi32.dll")]
    internal static extern uint SetBkColor(nint dc, uint color);
    [DllImport("gdi32.dll")]
    internal static extern int SetBkMode(nint dc, int mode);
    [DllImport("gdi32.dll")]
    internal static extern int RoundRect(nint dc, int left, int top, int right, int bottom, int width, int height);
    [DllImport("gdi32.dll", EntryPoint = "CreateFontW", CharSet = CharSet.Unicode)]
    internal static extern nint CreateFont(int height, int width, int escapement, int orientation, int weight,
        uint italic, uint underline, uint strikeOut, uint charSet, uint outputPrecision, uint clipPrecision, uint quality,
        uint pitchAndFamily, string face);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern int SetWindowTheme(nint window, string app, string? id);
}
