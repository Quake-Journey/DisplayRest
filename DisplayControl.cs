using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
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
    private nint _captureWindow;
    private bool _modifierOnly;
    private bool _off;
    private long _offRequestedUntil;
    private long _ignoreWakeUntil;
    private long _ignoreToggleUntil;
    private long _blockedWakeUntil;
    private long _lastPause;
    private bool _wakeQueued;

    internal event Action<Shortcut>? ShortcutCaptured;
    internal event Action<bool>? DisplayChanged;

    internal DisplayControl(UserSettings settings, Action<Action> postToUi)
    {
        _settings = settings;
        _postToUi = postToUi;
        _keyboardHook = KeyboardInput;
        _mouseHook = MouseInput;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _thread = new Thread(() => Run(started)) { IsBackground = true, Name = "DisplayRest input" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        started.Task.GetAwaiter().GetResult();
    }

    internal void Update(UserSettings settings) => Post(() => _settings = settings);

    internal void Capture(nint window) => Post(() => _captureWindow = window);

    internal void Toggle() => Post(ToggleDisplays);

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

    private nint KeyboardInput(int code, nint wParam, nint lParam)
    {
        if (code == 0 && ProcessKey(Marshal.ReadInt32(lParam), wParam is KeyDown or SystemKeyDown))
            return 1;
        return CallNextHookEx(0, code, wParam, lParam);
    }

    private bool ProcessKey(int key, bool down)
    {
        var now = Environment.TickCount64;
        bool isModifier = Shortcut.ModifierFor(key) != Modifiers.None;
        bool wasPressed = _forwardedKeys.TryGetValue(key, out bool forwarded);
        var mods = HeldModifiers();
        bool pause = key is Shortcut.Pause or 0x03; // Ctrl+Pause can arrive as VK_CANCEL.
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
            chord = new Shortcut(pause ? Shortcut.Pause : key, mods);
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
            _captureWindow = 0;
            var captured = chord;
            _postToUi(() => ShortcutCaptured?.Invoke(captured));
            block = true;
        }
        else if (chord is { } && chord == _settings.Shortcut)
        {
            if (IsOff)
                QueueWake();
            else if (now >= _ignoreToggleUntil)
                Toggle();
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
