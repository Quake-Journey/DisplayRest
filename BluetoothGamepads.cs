using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DisplayRest;

internal static unsafe class BluetoothGamepads
{
    private const uint DisconnectDevice = (0x41 << 16) | (3 << 2);
    private const int NoMoreItems = 259;
    internal sealed record Controller(ulong Address, string Name);

    internal static Controller[] Read()
    {
        var controllers = new Dictionary<ulong, Controller>();
        VisitConnected((_, device) => controllers.TryAdd(device.Address, device));
        var result = new Controller[controllers.Count];
        controllers.Values.CopyTo(result, 0);
        return result;
    }

    internal static void Disconnect(ulong selected)
    {
        var disconnected = new HashSet<ulong>();
        int firstError = 0;
        VisitConnected((radio, device) =>
        {
            if ((selected != 0 && selected != device.Address) || disconnected.Contains(device.Address))
                return;
            ulong address = device.Address;
            if (DeviceIoControl(radio, DisconnectDevice, &address, sizeof(ulong), 0, 0, out _, 0))
                disconnected.Add(address);
            else if (firstError == 0)
                firstError = Marshal.GetLastPInvokeError();
        });
        if (firstError != 0)
            throw new Win32Exception(firstError);
        if (disconnected.Count == 0 && selected != 0)
            throw new RestUnavailableException("GamepadTargetUnavailable");
    }

    internal static bool IsGamepad(uint deviceClass, string name)
    {
        var major = deviceClass & 0x1F00;
        if (major == 0x0500 && (deviceClass & 0x3C) is 0x04 or 0x08)
            return true;
        if ((deviceClass & 0xC0) != 0 || major is not (0 or 0x0500 or 0x1F00))
            return false;
        return name.Equals("Wireless Controller", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("DualSense Wireless Controller", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("DualShock 4", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Xbox Wireless Controller", StringComparison.OrdinalIgnoreCase);
    }

    private static void VisitConnected(Action<SafeFileHandle, Controller> visit)
    {
        uint size = sizeof(uint);
        var radios = BluetoothFindFirstRadio(in size, out var radio);
        if (radios == 0)
        {
            int error = Marshal.GetLastPInvokeError();
            radio?.Dispose();
            if (error is not (NoMoreItems or 2))
                throw new Win32Exception(error);
            return;
        }
        try
        {
            do
            {
                using (radio)
                {
                    var search = new Search { Size = (uint)sizeof(Search), Connected = 1, Radio = radio.DangerousGetHandle() };
                    var info = new DeviceInfo { Size = (uint)sizeof(DeviceInfo) };
                    nint devices = BluetoothFindFirstDevice(in search, ref info);
                    if (devices == 0)
                    {
                        int error = Marshal.GetLastPInvokeError();
                        if (error is not (NoMoreItems or 2))
                            throw new Win32Exception(error);
                        continue;
                    }
                    try
                    {
                        do
                        {
                            if (info.Connected == 0 || info.Address == 0)
                                continue;
                            string name = new(info.Name);
                            if (IsGamepad(info.DeviceClass, name))
                                visit(radio, new(info.Address, name));
                        }
                        while (BluetoothFindNextDevice(devices, ref info));
                    }
                    finally { BluetoothFindDeviceClose(devices); }
                }
            }
            while (BluetoothFindNextRadio(radios, out radio));
            radio?.Dispose();
        }
        finally { BluetoothFindRadioClose(radios); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Search
    {
        internal uint Size, Authenticated, Remembered, Unknown, Connected, Inquiry;
        internal byte Timeout;
        internal nint Radio;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfo
    {
        internal uint Size;
        internal ulong Address;
        internal uint DeviceClass, Connected, Remembered, Authenticated;
        internal fixed ushort LastSeenAndUsed[16];
        internal fixed char Name[248];
    }

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern nint BluetoothFindFirstRadio(in uint size, out SafeFileHandle radio);
    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern bool BluetoothFindNextRadio(nint find, out SafeFileHandle radio);
    [DllImport("bthprops.cpl")]
    private static extern bool BluetoothFindRadioClose(nint find);
    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern nint BluetoothFindFirstDevice(in Search search, ref DeviceInfo info);
    [DllImport("bthprops.cpl")]
    private static extern bool BluetoothFindNextDevice(nint find, ref DeviceInfo info);
    [DllImport("bthprops.cpl")]
    private static extern bool BluetoothFindDeviceClose(nint find);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, void* input, uint inputSize,
        nint output, uint outputSize, out uint returned, nint overlapped);
}
