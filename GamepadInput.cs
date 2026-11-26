using System;
using System.Runtime.InteropServices;
using static DisplayRest.WindowsNative;

namespace DisplayRest;

internal sealed class GamepadInput : IDisposable
{
    internal const int Timer = 2;
    private const uint ConnectedInterval = 32;
    private const uint CaptureInterval = 120;
    private const uint DisconnectedInterval = 750;
    private readonly nint _window;
    private readonly Action<GamepadGesture, GamepadButtons> _sample;
    private readonly RawGamepadInput? _raw;
    private readonly XInputSlot[] _slots = [new(), new(), new(), new()];
    private uint _interval;
    private bool _disposed;
    private bool _capturing;

    internal GamepadInput(nint window, Action<GamepadGesture, GamepadButtons> sample)
    {
        _window = window;
        _sample = sample;
        if (RawGamepadInput.Register(window, enable: true))
            _raw = new();
    }

    internal void Reset(bool capturing)
    {
        _capturing = capturing;
        _raw?.Reset();
        foreach (var slot in _slots)
        {
            slot.Gesture.Reset();
            slot.RetryAt = 0;
        }
        Schedule(ConnectedInterval);
    }

    internal void ReceiveRaw(nint input) => _raw?.Read(input, _sample);
    internal void Forget(nint device) => _raw?.Forget(device);

    internal void Poll()
    {
        bool connected = false;
        long now = Environment.TickCount64;
        uint retry = _capturing ? CaptureInterval : DisconnectedInterval;
        for (uint index = 0; index < _slots.Length && !_disposed; index++)
        {
            var slot = _slots[index];
            if (now < slot.RetryAt)
                continue;
            if (XInputGetState(index, out var state) != 0)
            {
                if (slot.Connected)
                {
                    slot.Connected = false;
                    slot.Buttons = GamepadButtons.None;
                    slot.Gesture.Synchronize(GamepadButtons.None);
                }
                slot.RetryAt = now + retry;
                continue;
            }
            connected = true;
            var buttons = ReadXInputButtons(state.Buttons, state.LeftTrigger, state.RightTrigger, slot.Buttons);
            if (!slot.Connected)
            {
                slot.Connected = true;
                slot.Gesture.Synchronize(buttons);
            }
            else if (buttons != slot.Buttons)
                _sample(slot.Gesture, buttons);
            slot.Buttons = buttons;
        }
        if (!_disposed)
            Schedule(connected ? ConnectedInterval : _capturing ? CaptureInterval : DisconnectedInterval);
    }

    internal static GamepadButtons ReadXInputButtons(ushort bits, byte left, byte right, GamepadButtons previous)
    {
        GamepadButtons result = GamepadButtons.None;
        if ((bits & 0x0001) != 0) result |= GamepadButtons.Up;
        if ((bits & 0x0002) != 0) result |= GamepadButtons.Down;
        if ((bits & 0x0004) != 0) result |= GamepadButtons.Left;
        if ((bits & 0x0008) != 0) result |= GamepadButtons.Right;
        if ((bits & 0x0010) != 0) result |= GamepadButtons.Start;
        if ((bits & 0x0020) != 0) result |= GamepadButtons.Back;
        if ((bits & 0x0040) != 0) result |= GamepadButtons.LeftStick;
        if ((bits & 0x0080) != 0) result |= GamepadButtons.RightStick;
        if ((bits & 0x0100) != 0) result |= GamepadButtons.LeftBumper;
        if ((bits & 0x0200) != 0) result |= GamepadButtons.RightBumper;
        if ((bits & 0x1000) != 0) result |= GamepadButtons.A;
        if ((bits & 0x2000) != 0) result |= GamepadButtons.B;
        if ((bits & 0x4000) != 0) result |= GamepadButtons.X;
        if ((bits & 0x8000) != 0) result |= GamepadButtons.Y;
        if (left >= ((previous & GamepadButtons.LeftTrigger) == 0 ? 30 : 22)) result |= GamepadButtons.LeftTrigger;
        if (right >= ((previous & GamepadButtons.RightTrigger) == 0 ? 30 : 22)) result |= GamepadButtons.RightTrigger;
        return result;
    }

    private void Schedule(uint interval)
    {
        if (_interval != interval && SetTimer(_window, Timer, interval) != 0)
            _interval = interval;
    }

    public void Dispose()
    {
        _disposed = true;
        if (_interval != 0)
            KillTimer(_window, Timer);
        if (_raw is not null)
            RawGamepadInput.Register(_window, enable: false);
    }

    private sealed class XInputSlot
    {
        internal readonly GamepadGesture Gesture = new();
        internal bool Connected;
        internal long RetryAt;
        internal GamepadButtons Buttons;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        internal uint Packet;
        internal ushort Buttons;
        internal byte LeftTrigger, RightTrigger;
        internal short LeftX, LeftY, RightX, RightY;
    }

    [DllImport("xinput1_4.dll")]
    private static extern uint XInputGetState(uint index, out XInputState state);
}
