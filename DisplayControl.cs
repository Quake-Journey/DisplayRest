using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using static DisplayRest.WindowsNative;

namespace DisplayRest;

internal sealed class DisplayControl : IDisposable
{
    private const int RunAction = AppMessage + 1;
    private const int StopThread = AppMessage + 2;
    private const int WakeDisplays = AppMessage + 3;
    private const int SleepAgain = AppMessage + 4;
    private readonly Action<Action> _postToUi;
    private readonly ConcurrentQueue<Action> _actions = new();
    private readonly Thread _thread;
    private readonly Dictionary<int, bool> _forwardedKeys = new();
    private readonly HashSet<int> _modifiers = new();
    private readonly HashSet<int> _blockedButtons = new();
    private readonly InputHook _keyboardHook;
    private readonly InputHook _mouseHook;
    private nint _window;
    private bool _sleepPending;
    private UserSettings _settings;
    private Dictionary<Shortcut, CommandId> _bindings;
    private GamepadInput? _gamepads;
    private GamepadButtons[] _gamepadBindings = [];
    private nint _captureWindow;
    private long _captureId;
    private bool _modifierOnly;
    private bool _off;
    private long _offRequestedUntil;
    private long _ignoreWakeUntil;
    private long _ignoreToggleUntil;
    private long _blockedWakeUntil;
    private long _lastPause;
    private bool _wakeQueued;

    internal event Action<Shortcut, long>? ShortcutCaptured;
    internal event Action<CommandId>? CommandPressed;
    internal event Action<bool>? DisplayChanged;

    internal DisplayControl(UserSettings settings, Action<Action> postToUi)
    {
        _settings = settings;
        _bindings = Bindings(settings);
        _postToUi = postToUi;
        _keyboardHook = KeyboardInput;
        _mouseHook = MouseInput;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _thread = new Thread(() => Run(started)) { IsBackground = true, Name = "DisplayRest input" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        started.Task.GetAwaiter().GetResult();
    }

    internal void Update(UserSettings settings)
    {
        var bindings = Bindings(settings);
        Post(() =>
        {
            var previous = _bindings;
            _settings = settings;
            _bindings = bindings;
            if (previous.Count != bindings.Count || previous.Any(entry =>
                !bindings.TryGetValue(entry.Key, out var command) || command != entry.Value))
                ConfigureGamepads();
        });
    }

    private static Dictionary<Shortcut, CommandId> Bindings(UserSettings settings) =>
        settings.Bindings().ToDictionary(binding => binding.Key, binding => binding.Command);

    internal void Capture(nint window, long id = 0) =>
        Post(() => SetCapture(window, id));

    private void SetCapture(nint window, long id)
    {
        if (_captureWindow == window && _captureId == id)
            return;
        _captureWindow = window;
        _captureId = id;
        ConfigureGamepads();
    }

    private void ConfigureGamepads()
    {
        _gamepadBindings = _bindings.Keys.Where(key => key.IsGamepad).Select(key => key.Gamepad).ToArray();
        if (_captureWindow == 0 && _gamepadBindings.Length == 0)
        {
            _gamepads?.Dispose();
            _gamepads = null;
            return;
        }
        _gamepads ??= new GamepadInput(_window, GamepadSample);
        _gamepads.Reset(_captureWindow != 0);
    }

    internal void Toggle() => Post(ToggleDisplays);

    internal Task TurnOffForPreset()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() => { TurnOff(); completion.SetResult(); });
        return completion.Task;
    }

    private void Post(Action action)
    {
        _actions.Enqueue(action);
        PostMessage(_window, RunAction);
    }

    private void Run(TaskCompletionSource started)
    {
        NativeWindow? window = null;
        nint keyboard = 0;
        nint mouse = 0;
        nint notification = 0;
        try
        {
            window = new NativeWindow("DisplayRest.Input", WindowMessage);
            _window = window.Handle;
            notification = RegisterPowerSettingNotification(_window, in SessionDisplayStatus, 0);
            if (notification == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var module = GetModuleHandle(0);
            keyboard = SetWindowsHookEx(KeyboardLowLevel, _keyboardHook, module, 0);
            if (keyboard == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            mouse = SetWindowsHookEx(MouseLowLevel, _mouseHook, module, 0);
            if (mouse == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            foreach (int modifier in Shortcut.ModifierKeys)
            {
                if (GetAsyncKeyState(modifier) >= 0)
                    continue;
                _modifiers.Add(modifier);
                _forwardedKeys[modifier] = true;
            }
            ConfigureGamepads();
            started.SetResult();
            NativeWindow.Pump();
        }
        catch (Exception error)
        {
            if (!started.TrySetException(error))
                _postToUi(() => PostQuitMessage(1));
        }
        finally
        {
            _gamepads?.Dispose();
            if (mouse != 0) UnhookWindowsHookEx(mouse);
            if (keyboard != 0) UnhookWindowsHookEx(keyboard);
            if (notification != 0) UnregisterPowerSettingNotification(notification);
            window?.Dispose();
        }
    }

    private nint? WindowMessage(nint window, int msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case RunAction:
                while (_actions.TryDequeue(out var action))
                    action();
                return 0;
            case StopThread:
                PostQuitMessage(0);
                return 0;
            case WakeDisplays:
                Wake();
                return 0;
            case SleepAgain:
                if (_off && !_wakeQueued)
                    SetDisplays(_window, on: false);
                return 0;
            case TimerMessage when wParam == 1:
                if (_sleepPending)
                    TurnOff();
                return 0;
            case TimerMessage when wParam == GamepadInput.Timer:
                _gamepads?.Poll();
                return 0;
            case RawGamepadInput.InputMessage:
                _gamepads?.ReceiveRaw(lParam);
                return null; // DefWindowProc cleans up foreground raw input.
            case RawGamepadInput.DeviceChangeMessage:
                if (wParam == 2) // GIDC_REMOVAL
                    _gamepads?.Forget(lParam);
                return null;
        }
        if (msg != PowerBroadcast || wParam != PowerSettingChange || lParam == 0 ||
            Marshal.PtrToStructure<Guid>(lParam) != SessionDisplayStatus || Marshal.ReadInt32(lParam, 16) != sizeof(int))
            return null;

        int state = Marshal.ReadInt32(lParam, 20);
        if (state == 1 && _off && Environment.TickCount64 < _blockedWakeUntil)
        {
            // Windows or the video driver may react before a blocked input reaches the hook.
            _blockedWakeUntil = 0;
            PostMessage(_window, SleepAgain);
            return 0;
        }
        if (state is 0 or 1)
        {
            _offRequestedUntil = 0;
            SetOffState(state == 0);
        }
        return 0;
    }

    private bool IsOff => _off || Environment.TickCount64 < _offRequestedUntil;

    private void ToggleDisplays()
    {
        if (IsOff)
            Wake();
        else if (_sleepPending)
            CancelSleep();
        else
            _sleepPending = SetTimer(_window, 1, 550) != 0;
    }

    private void CancelSleep()
    {
        if (!_sleepPending)
            return;
        KillTimer(_window, 1);
        _sleepPending = false;
    }

    private void TurnOff()
    {
        CancelSleep();
        if (IsOff)
            return;

        var now = Environment.TickCount64;
        _offRequestedUntil = now + 2200;
        _ignoreWakeUntil = now + 260;
        SetDisplays(_window, on: false);
    }

    private void Wake()
    {
        _wakeQueued = false;
        CancelSleep();
        _offRequestedUntil = 0;
        _blockedWakeUntil = 0;
        _ignoreToggleUntil = Environment.TickCount64 + 700;
        SetOffState(false);
        SetDisplays(_window, on: true);
    }

    private void SetOffState(bool value)
    {
        if (_off == value)
            return;
        _off = value;
        if (value)
            CancelSleep();
        _postToUi(() => DisplayChanged?.Invoke(value));
    }

    private void QueueWake()
    {
        if (_wakeQueued)
            return;
        _wakeQueued = true;
        PostMessage(_window, WakeDisplays);
    }

    private void GamepadSample(GamepadGesture gesture, GamepadButtons buttons)
    {
        bool capturing = _captureWindow != 0;
        if (capturing && _captureWindow != GetForegroundWindow())
        {
            gesture.Synchronize(buttons);
            return;
        }
        var chord = gesture.Update(buttons, capturing, _gamepadBindings);
        if (chord == GamepadButtons.None)
            return;
        var shortcut = new Shortcut(0, Modifiers.None, chord);
        if (capturing)
            CompleteCapture(shortcut);
        else if (_bindings.TryGetValue(shortcut, out var command))
            RunShortcut(command, Environment.TickCount64);
    }

    private void CompleteCapture(Shortcut shortcut)
    {
        long id = _captureId;
        SetCapture(0, 0);
        _postToUi(() => ShortcutCaptured?.Invoke(shortcut, id));
    }

    private void RunShortcut(CommandId command, long now)
    {
        if (command.Target != CommandTarget.Displays)
            _postToUi(() => CommandPressed?.Invoke(command));
        else if (IsOff)
            QueueWake();
        else if (now >= _ignoreToggleUntil)
            Toggle();
    }

    private nint KeyboardInput(int code, nint wParam, nint lParam)
    {
        if (code == 0)
        {
            int key = Marshal.ReadInt32(lParam);
            if (key == Shortcut.ControlBreak)
            {
                // Windows can report both keys as VK_CANCEL when Ctrl is held.
                int scanCode = Marshal.ReadInt32(lParam, 4);
                bool extended = (Marshal.ReadInt32(lParam, 8) & 1) != 0;
                key = scanCode switch
                {
                    0x46 => extended ? Shortcut.Pause : Shortcut.ScrollLock,
                    0xE046 => Shortcut.Pause,
                    _ => key
                };
            }
            if (ProcessKey(key, wParam is KeyDown or SystemKeyDown))
                return 1;
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    private bool ProcessKey(int key, bool down)
    {
        var now = Environment.TickCount64;
        bool isModifier = Shortcut.ModifierFor(key) != Modifiers.None;
        bool wasPressed = _forwardedKeys.TryGetValue(key, out bool forwarded);
        var mods = HeldModifiers();
        bool pause = key == Shortcut.Pause;
        if (down && pause && now - _lastPause >= 170)
            wasPressed = false; // Some keyboards send Pause without a break event.

        if (down)
        {
            if (isModifier)
            {
                if (_modifiers.Count == 0)
                    _modifierOnly = true;
                _modifiers.Add(key);
            }
            else
            {
                _modifierOnly = false;
            }
        }
        else if (isModifier)
        {
            _modifiers.Remove(key);
        }

        Shortcut? chord = null;
        if (!isModifier && down && !wasPressed)
            chord = new Shortcut(key, mods);
        else if (isModifier && !down && wasPressed && _modifierOnly)
        {
            chord = new Shortcut(0, mods);
            _modifierOnly = false; // One action per group of held modifiers.
        }

        if (down && pause)
            _lastPause = now;
        bool block;
        if (chord is { IsValid: true } && _captureWindow != 0 && _captureWindow == GetForegroundWindow())
        {
            CompleteCapture(chord);
            block = true;
        }
        else if (chord is { } && _bindings.TryGetValue(chord, out var command))
        {
            RunShortcut(command, now);
            block = true;
        }
        else if (IsOff)
        {
            if (down && !wasPressed)
                HandleWakeInput(_settings.WakeByKeyboard, now);
            block = true;
        }
        else
        {
            block = false;
        }

        // Release a key previously passed to Windows, even if the displays are off.
        if (!down)
        {
            _forwardedKeys.Remove(key);
            return wasPressed ? !forwarded : block;
        }
        if (wasPressed)
            return !forwarded || block;

        _forwardedKeys[key] = !block;
        return block;
    }

    private Modifiers HeldModifiers()
    {
        var result = Modifiers.None;
        foreach (int modifier in _modifiers)
            result |= Shortcut.ModifierFor(modifier);
        return result;
    }

    private nint MouseInput(int code, nint wParam, nint lParam)
    {
        if (code == 0 && ProcessMouse((int)wParam, lParam))
            return 1;
        return CallNextHookEx(0, code, wParam, lParam);
    }

    private bool ProcessMouse(int message, nint data)
    {
        // A wake click is consumed as a pair so it cannot click the foreground app.
        if (message is LeftButtonDown + 1 or RightButtonDown + 1 or MiddleButtonDown + 1 or XButtonDown + 1)
            return _blockedButtons.Remove(ButtonId(message - 1, data));
        if (!IsOff)
            return false;

        if (message is not (MouseMove or MouseWheel or HorizontalWheel or LeftButtonDown or RightButtonDown or MiddleButtonDown or XButtonDown))
            return false;
        if (message is LeftButtonDown or RightButtonDown or MiddleButtonDown or XButtonDown)
            _blockedButtons.Add(ButtonId(message, data));

        HandleWakeInput(_settings.WakeByMouse, Environment.TickCount64);
        return true;
    }

    private void HandleWakeInput(bool enabled, long now)
    {
        if (enabled && now >= _ignoreWakeUntil)
            QueueWake();
        else
            _blockedWakeUntil = now + 450;
    }

    private static int ButtonId(int message, nint data) =>
        message == XButtonDown ? message + (Marshal.ReadInt32(data, 8) >> 16) * 16 : message;

    public void Dispose()
    {
        PostMessage(_window, StopThread);
        _thread.Join(TimeSpan.FromSeconds(1));
    }
}
