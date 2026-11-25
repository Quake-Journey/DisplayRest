using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security;
using System.Text.Json;
using System.Threading;
using static DisplayRest.WindowsNative;

namespace DisplayRest;

internal sealed class App
{
    private const string Instance = @"Local\DisplayRest-23E24AF4-B771-4A7E-A2C6-3B7753BD5680";
    internal const string AppId = "DisplayRest.DisplayRest";
    private const int ProcessActions = AppMessage + 1;
    private const int TrayCallback = AppMessage + 2;
    private const int OpenCommand = 1;
    private const int ToggleCommand = 2;
    private const int ExitCommand = 3;

    private readonly ConcurrentQueue<Action> _messages = new();
    private UserSettings _settings = new();
    private NativeWindow _host = null!;
    private DisplayControl _control = null!;
    private MainWindow? _window;
    private int _taskbarCreated;
    private bool _displaysOff;
    private bool _settingsError;
    private bool _taskbarIdSet;
    private bool _jumpListApplied;
    private bool _exitRequested;
    private bool _showingDuplicateWarning;
    private AutomaticUpdate? _updates;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 0 && args[0] == "--update-worker")
            return UpdateWorker.Run(Instance, args);
        if (args.Contains("--exit", StringComparer.OrdinalIgnoreCase))
        {
            if (EventWaitHandle.TryOpenExisting(Instance + "-exit", out var signal))
            {
                using (signal)
                    signal.Set();
            }
            return 0;
        }
        bool resume = args.Contains("--resume-tray", StringComparer.OrdinalIgnoreCase);
        if (!resume && EventWaitHandle.TryOpenExisting(AutomaticUpdate.InstallName(Instance), out var installing))
        {
            using (installing)
            {
                if (!installing.WaitOne(15000))
                    return 1;
            }
        }
        using var open = new EventWaitHandle(false, EventResetMode.AutoReset, Instance + "-open");
        using var instance = new Mutex(false, Instance, out bool primary);
        // Version 1.0's existing startup registration uses --tray.
        bool loginStartup = args.Contains("--tray", StringComparer.OrdinalIgnoreCase);
        if (!primary)
        {
            if (!loginStartup && !resume)
                open.Set();
            return 0;
        }
        using var exit = new EventWaitHandle(false, EventResetMode.AutoReset, Instance + "-exit");
        return new App().Run(open, exit, resume);
    }

    private int Run(EventWaitHandle open, EventWaitHandle exit, bool resume)
    {
        try { _settings = UserSettings.Load(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or SecurityException)
        {
            _settingsError = true;
        }

        UiText.Use(_settings.Language);
        _taskbarIdSet = SetCurrentProcessExplicitAppUserModelID(AppId) == 0;
        try
        {
            using var host = new NativeWindow("DisplayRest.Host", HostMessage);
            _host = host;
            using var control = new DisplayControl(_settings, Post);
            _control = control;
            _control.ShortcutCaptured += shortcut => _window?.AcceptShortcut(shortcut);
            _control.DisplayChanged += off =>
            {
                _displaysOff = off;
                _window?.DisplayChanged(off);
            };

            _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
            TrayIcon(0);
            var openWait = ThreadPool.RegisterWaitForSingleObject(open, (_, _) => Post(ShowDuplicateWarning), null, Timeout.Infinite, false);
            var exitWait = ThreadPool.RegisterWaitForSingleObject(exit, (_, _) => Post(Exit), null, Timeout.Infinite, false);
            try
            {
                if (!resume && !_settings.StartInTray)
                    OpenWindow();
                ConfigureUpdateTimer();
                if (resume)
                    SetTimer(_host.Handle, 3, 10000);
                NativeWindow.Pump(() => _window?.Handle ?? 0, message => _window?.FilterMessage(message) == true);
            }
            finally
            {
                exitWait.Unregister(null);
                openWait.Unregister(null);
                _updates?.Dispose();
                DestroySettingsWindow();
                TrayIcon(2);
            }
            return 0;
        }
        catch (Win32Exception error)
        {
            MessageBox(0, UiText.Get("DisplayControlError", error.NativeErrorCode), "DisplayRest", 0x10);
            return 1;
        }
    }

    private void Post(Action action)
    {
        _messages.Enqueue(action);
        PostMessage(_host.Handle, ProcessActions);
    }

    private nint? HostMessage(nint window, int message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case ProcessActions:
                while (!_exitRequested && _messages.TryDequeue(out var action))
                    action();
                return 0;
            case TrayCallback:
                if (lParam is 0x0202 or 0x0203)
                    OpenWindow();
                else if (lParam is 0x0205 or 0x007B)
                    TrayMenu();
                return 0;
            case CloseMessage:
                Exit();
                return 0;
            case TimerMessage when wParam == 2:
                if (_updates is not null)
                    SetTimer(window, 2, _updates.CheckWhenIdle());
                return 0;
            case TimerMessage when wParam == 3:
                KillTimer(window, 3);
                AutomaticUpdate.CleanWorkerCopy();
                return 0;
            case 0x0016 when wParam != 0: // WM_ENDSESSION
                Exit();
                return 0;
            default:
                if (_taskbarCreated != 0 && message == _taskbarCreated)
                    TrayIcon(0);
                return null;
        }
    }

    private void TrayIcon(uint operation)
    {
        var data = new NotifyIconData
        {
            Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NotifyIconData>(),
            Window = _host.Handle,
            Id = 1,
            Flags = 7, // callback, icon, tooltip
            Callback = TrayCallback,
            Icon = NativeWindow.Icon,
            Tip = "DisplayRest"
        };
        ShellNotifyIcon(operation, ref data);
    }

    private void TrayMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == 0)
            return;
        try
        {
            AppendMenu(menu, 0, OpenCommand, UiText.Get("MenuOpen"));
            AppendMenu(menu, 0, ToggleCommand, UiText.Get(_displaysOff ? "MenuDisplayOn" : "MenuDisplayOff"));
            AppendMenu(menu, 0x800, 0, null);
            AppendMenu(menu, 0, ExitCommand, UiText.Get("MenuExit"));
            GetCursorPos(out var point);
            SetForegroundWindow(_host.Handle);
            var command = TrackPopupMenu(menu, 0x102, point.X, point.Y, 0, _host.Handle, 0);
            PostMessage(_host.Handle, 0);
            switch (command)
            {
                case OpenCommand: OpenWindow(); break;
                case ToggleCommand: _control.Toggle(); break;
                case ExitCommand: Exit(); break;
            }
        }
        finally { DestroyMenu(menu); }
    }

    private void OpenWindow()
    {
        try
        {
            _window ??= new MainWindow(_control, _settings, _displaysOff, _settingsError, UpdateSettings,
                minimize: DestroySettingsWindow, close: CloseSettingsWindow);
            ApplyTaskbarMenu();
            _window.Show();
        }
        catch (Win32Exception error)
        {
            MessageBox(0, UiText.Get("DisplayControlError", error.NativeErrorCode), "DisplayRest", 0x10);
        }
    }

    private void ShowDuplicateWarning()
    {
        if (_showingDuplicateWarning)
            return;
        _showingDuplicateWarning = true;
        try
        {
            OpenWindow();
            MessageBox(_window?.Handle ?? 0, UiText.Get("AlreadyRunning"), "DisplayRest", 0x30);
        }
        finally { _showingDuplicateWarning = false; }
    }

    private void ConfigureUpdateTimer()
    {
        if (_settings.AutoUpdate)
        {
            _updates ??= new AutomaticUpdate(Instance, Post,
                () => _settings.AutoUpdate && !_exitRequested && _window is null && !_displaysOff, Exit);
            SetTimer(_host.Handle, 2, 15000);
        }
        else
        {
            KillTimer(_host.Handle, 2);
            _updates?.Dispose();
            _updates = null;
        }
    }

    private void UpdateSettings(UserSettings settings)
    {
        bool languageChanged = settings.Language != _settings.Language;
        bool updatesChanged = settings.AutoUpdate != _settings.AutoUpdate;
        _settings = settings;
        _settingsError = false;
        _control.Update(settings);
        if (updatesChanged)
            ConfigureUpdateTimer();
        if (languageChanged)
        {
            _jumpListApplied = false;
            ApplyTaskbarMenu();
        }
    }

    private void ApplyTaskbarMenu()
    {
        if (_taskbarIdSet && !_jumpListApplied)
            _jumpListApplied = TaskbarMenu.SetExitTask(AppId, UiText.Get("MenuExit"));
    }

    private void CloseSettingsWindow()
    {
        if (_settings.CloseToTray)
            DestroySettingsWindow();
        else
            Exit();
    }

    private void DestroySettingsWindow()
    {
        var window = _window;
        _window = null;
        window?.Dispose();
    }

    private void Exit()
    {
        _exitRequested = true;
        PostQuitMessage(0);
    }
}
