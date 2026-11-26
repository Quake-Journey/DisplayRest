using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DisplayRest;

internal sealed unsafe class RawGamepadInput
{
    internal const int InputMessage = 0x00FF;
    internal const int DeviceChangeMessage = 0x00FE;
    private const uint HidType = 2;
    private const ushort DesktopPage = 1;
    private const ushort ButtonPage = 9;
    private const ushort Hat = 0x39;
    private const int HidSuccess = 0x00110000;
    private const int MaxInputBytes = 65536;

    private readonly Dictionary<nint, Controller?> _devices = new();
    private byte[] _buffer = new byte[512];

    internal static bool Register(nint window, bool enable)
    {
        uint flags = enable ? 0x2100u : 1u; // INPUTSINK | DEVNOTIFY, or REMOVE
        nint target = enable ? window : 0;
        RawDevice* devices = stackalloc RawDevice[2]
        {
            new() { Page = DesktopPage, Usage = 4, Flags = flags, Window = target },
            new() { Page = DesktopPage, Usage = 5, Flags = flags, Window = target }
        };
        return RegisterRawInputDevices(devices, 2, (uint)sizeof(RawDevice));
    }

    internal void Forget(nint device) => _devices.Remove(device);

    internal void Reset()
    {
        foreach (var device in _devices.Values)
            device?.Gesture.Reset();
    }

    internal void Read(nint input, Action<GamepadGesture, GamepadButtons> sample)
    {
        const uint readInput = 0x10000003;
        uint headerBytes = (uint)sizeof(RawHeader);
        uint size = 0;
        if (GetRawInputData(input, readInput, null, ref size, headerBytes) != 0 ||
            size < headerBytes + 8 || size > MaxInputBytes)
            return;

        if (_buffer.Length < size)
            _buffer = new byte[size];
        fixed (byte* buffer = _buffer)
        {
            uint copied = GetRawInputData(input, readInput, buffer, ref size, headerBytes);
            var header = (RawHeader*)buffer;
            if (copied < headerBytes + 8 || copied > _buffer.Length || header->Type != HidType || header->Device == 0)
                return;

            uint* hid = (uint*)(buffer + headerBytes);
            uint bytes = hid[0], count = hid[1];
            if (bytes == 0 || count > (copied - headerBytes - 8) / bytes)
                return;

            if (!_devices.TryGetValue(header->Device, out var device))
                _devices.Add(header->Device, device = Controller.Open(header->Device));
            if (device is null)
                return;

            byte* report = (byte*)(hid + 2);
            for (uint i = 0; i < count; i++, report += bytes)
                if (device.Read(report, bytes))
                    sample(device.Gesture, device.Buttons);
        }
    }

    internal static GamepadButtons Button(bool playStation, int usage)
    {
        if (playStation && usage is >= 1 and <= 15)
            return usage switch
            {
                1 => GamepadButtons.X, 2 => GamepadButtons.A, 3 => GamepadButtons.B, 4 => GamepadButtons.Y,
                5 => GamepadButtons.LeftBumper, 6 => GamepadButtons.RightBumper,
                7 => GamepadButtons.LeftTrigger, 8 => GamepadButtons.RightTrigger,
                9 => GamepadButtons.Back, 10 => GamepadButtons.Start,
                11 => GamepadButtons.LeftStick, 12 => GamepadButtons.RightStick,
                13 => GamepadButtons.Guide, 14 => GamepadButtons.Touchpad, _ => GamepadButtons.Mute
            };
        return usage is >= 1 and <= 32 ? (GamepadButtons)(1UL << (GamepadLabels.GenericStart + usage - 1)) : 0;
    }

    internal static GamepadButtons Directions(int value, int min, int max)
    {
        if (value < min || value > max)
            return GamepadButtons.None;
        int index = (value - min) * (max - min == 3 ? 2 : 1);
        return index switch
        {
            0 => GamepadButtons.Up,
            1 => GamepadButtons.Up | GamepadButtons.Right,
            2 => GamepadButtons.Right,
            3 => GamepadButtons.Right | GamepadButtons.Down,
            4 => GamepadButtons.Down,
            5 => GamepadButtons.Down | GamepadButtons.Left,
            6 => GamepadButtons.Left,
            7 => GamepadButtons.Left | GamepadButtons.Up,
            _ => GamepadButtons.None
        };
    }

    private sealed class Controller(byte[] descriptor, bool playStation, int hatMin, int hatMax, int usageCapacity)
    {
        internal readonly GamepadGesture Gesture = new();
        private readonly ushort[] _usages = new ushort[usageCapacity];
        private GamepadButtons _buttons;
        private GamepadButtons _directions;
        internal GamepadButtons Buttons => _buttons | _directions;

        internal static Controller? Open(nint device)
        {
            if (IsXInput(device))
                return null;

            const uint readDeviceInfo = 0x2000000B, readPreparsedData = 0x20000005;
            uint infoBytes = (uint)sizeof(DeviceInfo);
            var info = new DeviceInfo { Size = infoBytes };
            if (GetRawInputDeviceInfo(device, readDeviceInfo, &info, ref infoBytes) == uint.MaxValue || info.Type != HidType)
                return null;

            uint bytes = 0;
            if (GetRawInputDeviceInfo(device, readPreparsedData, null, ref bytes) != 0 || bytes is 0 or > MaxInputBytes)
                return null;

            var descriptor = new byte[bytes];
            fixed (byte* data = descriptor)
            {
                uint copied = GetRawInputDeviceInfo(device, readPreparsedData, data, ref bytes);
                if (copied == uint.MaxValue || copied == 0)
                    return null;

                var capacity = HidP_MaxUsageListLength(0, ButtonPage, data);
                if (capacity > 1024)
                    return null;
                ushort hats = 1;
                var caps = new ValueCaps();
                bool hasRange = HidP_GetSpecificValueCaps(0, DesktopPage, 0, Hat, &caps, ref hats, data) == HidSuccess &&
                    (long)caps.Max - caps.Min is 3 or 7;
                bool sony = info.Vendor == 0x054C && info.Product is 0x05C4 or 0x09CC or 0x0BA0 or 0x0CE6 or 0x0DF2;
                return new Controller(descriptor, sony, hasRange ? caps.Min : 0, hasRange ? caps.Max : 7, (int)capacity);
            }
        }

        internal bool Read(byte* report, uint bytes)
        {
            var before = Buttons;
            fixed (byte* data = descriptor)
            fixed (ushort* usages = _usages)
            {
                uint count = (uint)_usages.Length;
                if (count != 0 && HidP_GetUsages(0, ButtonPage, 0, usages, ref count, data, report, bytes) == HidSuccess)
                {
                    _buttons = GamepadButtons.None;
                    for (int i = 0; i < count; i++)
                        _buttons |= Button(playStation, usages[i]);
                }
                if (HidP_GetUsageValue(0, DesktopPage, 0, Hat, out uint position, data, report, bytes) == HidSuccess)
                    _directions = Directions(unchecked((int)position), hatMin, hatMax);
            }
            return Buttons != before;
        }
    }

    private static bool IsXInput(nint device)
    {
        const uint readName = 0x20000007;
        uint chars = 0;
        if (GetRawInputDeviceInfo(device, readName, null, ref chars) != 0 || chars is 0 or > 2048)
            return false;
        int capacity = (int)chars;
        var name = stackalloc char[capacity];
        uint copied = GetRawInputDeviceInfo(device, readName, name, ref chars);
        if (copied is 0 or uint.MaxValue)
            return false;
        var path = new ReadOnlySpan<char>(name, capacity);
        int end = path.IndexOf('\0');
        return (end >= 0 ? path[..end] : path).Contains("IG_", StringComparison.OrdinalIgnoreCase);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawDevice { internal ushort Page, Usage; internal uint Flags; internal nint Window; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawHeader { internal uint Type, Size; internal nint Device, WParam; }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct DeviceInfo
    {
        [FieldOffset(0)] internal uint Size;
        [FieldOffset(4)] internal uint Type;
        [FieldOffset(8)] internal uint Vendor;
        [FieldOffset(12)] internal uint Product;
    }

    [StructLayout(LayoutKind.Explicit, Size = 72)]
    private struct ValueCaps
    {
        [FieldOffset(40)] internal int Min;
        [FieldOffset(44)] internal int Max;
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterRawInputDevices(RawDevice* devices, uint count, uint size);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(nint input, uint command, void* data, ref uint bytes, uint headerBytes);

    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW")]
    private static extern uint GetRawInputDeviceInfo(nint device, uint command, void* data, ref uint bytes);

    [DllImport("hid.dll")]
    private static extern uint HidP_MaxUsageListLength(int type, ushort page, byte* data);

    [DllImport("hid.dll")]
    private static extern int HidP_GetSpecificValueCaps(int type, ushort page, ushort collection, ushort usage,
        ValueCaps* caps, ref ushort count, byte* data);

    [DllImport("hid.dll")]
    private static extern int HidP_GetUsages(int type, ushort page, ushort collection, ushort* usages,
        ref uint count, byte* data, byte* report, uint bytes);

    [DllImport("hid.dll")]
    private static extern int HidP_GetUsageValue(int type, ushort page, ushort collection, ushort usage,
        out uint value, byte* data, byte* report, uint bytes);
}
