using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static DisplayRest.WindowsNative;

namespace DisplayRest;

internal sealed class App
{
    internal const string Name = "PC Rest";
    private const string Instance = @"Local\DisplayRest-23E24AF4-B771-4A7E-A2C6-3B7753BD5680";
    internal const string AppId = "DisplayRest.DisplayRest";
    private const int ProcessActions = AppMessage + 1;
    private const int TrayCallback = AppMessage + 2;
    private const int OpenCommand = 1;
    private const int ToggleCommand = 2;
    private const int ExitCommand = 3;
    private const int FirstPresetCommand = 20;

    private readonly ConcurrentQueue<Action> _messages = new();
    private UserSettings _settings = new();
    private NativeWindow _host = null!;
    private DisplayControl _control = null!;
    private MainWindow? _window;
    private BlackScreen? _blackScreen;
    private int _taskbarCreated;
    private bool _displaysOff;
    private bool _settingsError;
    private bool _taskbarIdSet;
    private bool _jumpListApplied;
    private bool _exitRequested;
    private bool _showingDuplicateWarning;
    private AutomaticUpdate? _updates;
    private bool _runningActions;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args is ["--check-update"])
            return UpdateWorker.Check();
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
            _control.ShortcutCaptured += (shortcut, id) => _window?.AcceptShortcut(shortcut, id);
            _control.CommandPressed += command => Execute(command, confirm: false);
            _control.BlackScreenDismissed += CloseBlackScreen;
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
                ConfigureUpdateTimer();
                if (!resume && !_settings.StartInTray)
                    OpenWindow();
                if (resume)
                    SetTimer(_host.Handle, 3, 10000);
                NativeWindow.Pump(() => _blackScreen is null ? _window?.Handle ?? 0 : 0,
                    message => _blackScreen is null && _window?.FilterMessage(message) == true);
            }
            finally
            {
                exitWait.Unregister(null);
                openWait.Unregister(null);
                _updates?.Dispose();
                CloseBlackScreen();
                DestroySettingsWindow();
                TrayIcon(2);
            }
            return 0;
        }
        catch (Win32Exception error)
        {
            MessageBox(0, UiText.Get("DisplayControlError", error.NativeErrorCode), Name, 0x10);
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
                // A manual offer runs a modal message loop. Do not re-enter the scheduler.
                KillTimer(window, 2);
                if (_updates?.Poll() is uint delay && delay != 0 && !_exitRequested)
                    SetTimer(window, 2, delay);
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
            Tip = Name
        };
        ShellNotifyIcon(operation, ref data);
    }

    private void ShowActionFailure(string message)
    {
        if (_window is { } window)
        {
            window.ReportAction(message, error: true);
            return;
        }
        var data = new NotifyIconData
        {
            Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NotifyIconData>(),
            Window = _host.Handle,
            Id = 1,
            Flags = 0x10, // NIF_INFO
            Info = message[..Math.Min(message.Length, 255)],
            InfoTitle = Name,
            InfoFlags = 2
        };
        ShellNotifyIcon(1, ref data);
    }

    private void Execute(CommandId command, bool confirm)
    {
        if (_exitRequested)
            return;
        if (command.Target == CommandTarget.BlackScreen ||
            (command.Target == CommandTarget.Preset && _settings.FindPreset(command.Preset)?.Actions == RestAction.BlackScreen))
        {
            if (_blackScreen is not null)
                CloseBlackScreen();
            else
            {
                try { ShowBlackScreen(); }
                catch (Win32Exception error) { ShowActionFailure(UiText.Get("ActionNativeError", error.NativeErrorCode)); }
            }
            return;
        }
        if (_runningActions)
            return;
        if (command.Target == CommandTarget.Displays)
        {
            _control.Toggle();
            return;
        }
        RestRequest? request = command.Target switch
        {
            CommandTarget.Sleep => new(RestAction.Sleep),
            CommandTarget.Hdr => new(RestAction.Hdr),
            CommandTarget.Gamepads => new(RestAction.Gamepads),
            CommandTarget.Preset when _settings.FindPreset(command.Preset) is { } preset => new(preset.Actions, preset.HdrMode),
            _ => null
        };
        if (request is null)
            return;
        var settings = _settings;
        _runningActions = true;
        if (confirm && (request.Actions & RestAction.Sleep) != 0 &&
            MessageBox(_window?.Handle ?? 0, UiText.Get("ConfirmSleep"), Name, 0x124) != 6)
        {
            _runningActions = false;
            return;
        }
        _ = Task.Run(async () =>
        {
            // Allow the hotkey or the mouse button that invoked the command to be released.
            await Task.Delay(550).ConfigureAwait(false);
            var failures = new List<(RestAction Action, string Key, int? Code)>();
            foreach (var action in request.Steps())
            {
                try
                {
                    switch (action)
                    {
                        case RestAction.Gamepads: BluetoothGamepads.Disconnect(settings.GamepadTarget); break;
                        case RestAction.Hdr: HdrDisplays.Set(settings.HdrTarget, request.HdrMode); break;
                        case RestAction.BlackScreen: await ShowBlackScreenForPreset().ConfigureAwait(false); break;
                        case RestAction.Lock: PcPower.Lock(); break;
                        case RestAction.Displays: await _control.TurnOffForPreset().ConfigureAwait(false); break;
                        case RestAction.Sleep: PcPower.Sleep(); break;
                    }
                }
                catch (RestUnavailableException error) { failures.Add((action, error.Key, null)); }
                catch (Win32Exception error) { failures.Add((action, "ActionNativeError", error.NativeErrorCode)); }
                catch (Exception error) when (error is EntryPointNotFoundException or DllNotFoundException)
                { failures.Add((action, "ActionUnsupported", null)); }
            }
            Post(() =>
            {
                _runningActions = false;
                _window?.RefreshActionDevices();
                if (failures.Count == 0)
                    return;
                var messages = failures.Select(item =>
                    $"{UiText.Get(ActionText(item.Action))} — {UiText.Get(item.Key, item.Code)}");
                ShowActionFailure(string.Join(Environment.NewLine, messages));
            });
        });
    }

    internal static string ActionText(RestAction action) => action switch
    {
        RestAction.Sleep => "PcSleep",
        RestAction.Hdr => "Hdr",
        RestAction.Gamepads => "GamepadDisconnect",
        RestAction.Lock => "PresetLock",
        RestAction.BlackScreen => "BlackScreen",
        _ => "PresetDisplays"
    };

    private void ShowBlackScreen()
    {
        _blackScreen ??= new BlackScreen(() => Post(CloseBlackScreen));
        _control.BlackScreenVisible = true;
    }

    private Task ShowBlackScreenForPreset()
    {
        var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try { ShowBlackScreen(); shown.SetResult(); }
            catch (Win32Exception error) { shown.SetException(error); }
        });
        return shown.Task;
    }

    private void CloseBlackScreen()
    {
        _control.BlackScreenVisible = false;
        _blackScreen?.Dispose();
        _blackScreen = null;
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
            var presets = _settings.PresetList;
            if (presets.Length != 0)
                AppendMenu(menu, 0x800, 0, null);
            for (int index = 0; index < presets.Length; index++)
                AppendMenu(menu, 0, (nuint)(FirstPresetCommand + index), presets[index].Name.Replace("&", "&&"));
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
                default:
                    uint index = command - FirstPresetCommand;
                    if (index < presets.Length)
                        Execute(new(CommandTarget.Preset, presets[index].Id), confirm: true);
                    break;
            }
        }
        finally { DestroyMenu(menu); }
    }

    private void OpenWindow()
    {
        try
        {
            _window ??= new MainWindow(_control, _settings, _displaysOff, _settingsError, UpdateSettings,
                Execute, CheckUpdates,
                minimize: DestroySettingsWindow, close: CloseSettingsWindow, exit: Exit);
            if (_updates is not null)
                _window.ShowUpdateStatus(_updates.Status);
            ApplyTaskbarMenu();
            _window.Show();
        }
        catch (Win32Exception error)
        {
            MessageBox(0, UiText.Get("DisplayControlError", error.NativeErrorCode), Name, 0x10);
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
            MessageBox(_window?.Handle ?? 0, UiText.Get("AlreadyRunning"), Name, 0x30);
        }
        finally { _showingDuplicateWarning = false; }
    }

    private void ConfigureUpdateTimer()
    {
        _updates ??= new AutomaticUpdate(Instance, Post, CanInstallUpdate, OfferUpdate,
            status => _window?.ShowUpdateStatus(status), Exit);
        _updates.Configure(_settings.AutoUpdate, _settings.UpdateIntervalHours);
        SetTimer(_host.Handle, 2, 250);
    }

    private void CheckUpdates()
    {
        _updates!.CheckManually();
        SetTimer(_host.Handle, 2, 250);
    }

    private bool CanInstallUpdate(bool manual) => !_exitRequested && !_runningActions && !_displaysOff &&
        _blackScreen is null && (manual ? _window?.HasPendingEdits != true : _settings.AutoUpdate && _window is null);

    private bool OfferUpdate(string version)
    {
        var window = _window;
        if (window is null || _exitRequested || _displaysOff || _blackScreen is not null)
            return false;
        string prompt = window.HasPendingEdits ? "UpdateOfferDraft" : "UpdateOffer";
        if (MessageBox(window.Handle, UiText.Get(prompt, version), Name, 0x124) != 6 || _window != window || _exitRequested)
            return false;
        if (window.HasPendingEdits)
            DestroySettingsWindow();
        return true;
    }

    private void UpdateSettings(UserSettings settings)
    {
        bool languageChanged = settings.Language != _settings.Language;
        bool updatesChanged = settings.AutoUpdate != _settings.AutoUpdate || settings.UpdateIntervalHours != _settings.UpdateIntervalHours;
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
        if (!_exitRequested && _updates?.WaitingToInstall == true)
            SetTimer(_host.Handle, 2, 250);
    }

    private void Exit()
    {
        _exitRequested = true;
        PostQuitMessage(0);
    }
}
