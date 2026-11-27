using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using static DisplayRest.WindowsNative;

namespace DisplayRest;

internal sealed class BlackScreen : IDisposable
{
    private const int Resize = AppMessage + 1;
    private readonly NativeWindow _window;
    private readonly Action _dismiss;
    private readonly nint _previousCursor;

    internal BlackScreen(Action dismiss)
    {
        _dismiss = dismiss;
        _previousCursor = GetCursor();
        // Create this window in physical pixels even when the settings UI uses system DPI.
        var previousDpi = SetThreadDpiAwarenessContext(-4); // PER_MONITOR_AWARE_V2
        try
        {
            _window = new NativeWindow("PCRest.BlackScreen", HandleMessage, extendedStyle: 0x80); // WS_EX_TOOLWINDOW
            try
            {
                int squareCorners = 1; // DWMWCP_DONOTROUND
                DwmSetWindowAttribute(_window.Handle, 33, in squareCorners, sizeof(int));
                if (!CoverDesktop(_window.Handle))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                SetForegroundWindow(_window.Handle);
                SetCursor(0);
            }
            catch { _window.Dispose(); throw; }
        }
        finally { SetThreadDpiAwarenessContext(previousDpi); }
    }

    private static bool CoverDesktop(nint window) => SetWindowPos(window, -1, // HWND_TOPMOST
        GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79),
        0x0040 | 0x0010); // SHOWWINDOW | NOACTIVATE

    private nint? HandleMessage(nint window, int message, nint w, nint l)
    {
        switch (message)
        {
            case PaintMessage:
                var dc = BeginPaint(window, out var paint);
                FillRect(dc, in paint.Area, GetStockObject(4)); // BLACK_BRUSH
                EndPaint(window, in paint);
                return 0;
            case 0x0014: return 1; // WM_ERASEBKGND
            case 0x0020: SetCursor(0); return 1; // WM_SETCURSOR
            case 0x007E or 0x02E0: // WM_DISPLAYCHANGE, WM_DPICHANGED
                PostMessage(window, Resize);
                return 0;
            case Resize:
                if (!CoverDesktop(window))
                    _dismiss();
                return 0;
            case CloseMessage:
            case 0x0202 or 0x0205: // Left or right button released.
                _dismiss();
                return 0;
        }
        return null;
    }

    public void Dispose()
    {
        _window.Dispose();
        SetCursor(_previousCursor);
    }

    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern nint SetCursor(nint cursor);
    [DllImport("user32.dll")] private static extern nint GetCursor();
}
