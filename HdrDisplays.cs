using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;

namespace DisplayRest;

internal static unsafe class HdrDisplays
{
    private const uint OnlyActivePaths = 2;
    private const int InsufficientBuffer = 122;
    private const int NotSupported = 50;
    private const int InvalidParameter = 87;
    private const int GetTargetName = 2;
    private const int GetAdvancedColor = 9;
    private const int SetAdvancedColor = 10;
    private const int GetAdvancedColor2 = 15;
    private const int SetHdr = 16;

    internal sealed record Monitor(string Key, string Name, bool Enabled, bool UsingHdr, ColorTarget Target);
    internal readonly record struct ColorTarget(uint Low, int High, uint Id, bool Modern);

    internal static Monitor[] Read()
    {
        var paths = ActivePaths();
        var results = new List<Monitor>();
        foreach (var path in paths)
        {
            var target = path.Target;
            if (target.Available == 0)
                continue;
            var info = new ColorInfo { Header = MakeHeader(GetAdvancedColor2, sizeof(ColorInfo), target.Adapter, target.Id) };
            int status = DisplayConfigGetDeviceInfo(&info);
            bool modern = status == 0;
            if (!modern)
            {
                if (status is not (NotSupported or InvalidParameter))
                    continue;
                info = new ColorInfo { Header = MakeHeader(GetAdvancedColor, sizeof(ColorInfo) - sizeof(uint), target.Adapter, target.Id) };
                if (DisplayConfigGetDeviceInfo(&info) != 0)
                    continue;
            }

            // The legacy packet exposes advanced color rather than separate HDR and WCG flags.
            bool supported = modern ? (info.Flags & 0x10) != 0 : (info.Flags & 0x0D) == 1;
            if (!supported)
                continue;
            var name = new TargetName { Header = MakeHeader(GetTargetName, sizeof(TargetName), target.Adapter, target.Id) };
            if (DisplayConfigGetDeviceInfo(&name) != 0 || name.DevicePath[0] == '\0')
                continue;
            string key = new(name.DevicePath);
            if (results.Any(result => StringComparer.OrdinalIgnoreCase.Equals(result.Key, key)))
                continue;
            string friendly = name.FriendlyName[0] != '\0' ? new string(name.FriendlyName) : $"#{results.Count + 1}";
            bool enabled = (info.Flags & (modern ? 0x20u : 2u)) != 0;
            results.Add(new(key, friendly, enabled, modern ? info.Mode == 2 : enabled,
                new(target.Adapter.Low, target.Adapter.High, target.Id, modern)));
        }
        return results.ToArray();
    }

    internal static Monitor[] Select(Monitor[] monitors, string? key) => key is null ? monitors :
        Array.FindAll(monitors, monitor => StringComparer.OrdinalIgnoreCase.Equals(monitor.Key, key));

    internal static bool DesiredState(HdrMode mode, Monitor[] monitors) =>
        mode == HdrMode.On || (mode == HdrMode.Toggle && Array.Exists(monitors, monitor => !monitor.Enabled));

    internal static void Set(string? key, HdrMode mode)
    {
        var monitors = Select(Read(), key);
        if (monitors.Length == 0)
            throw new RestUnavailableException(key is null ? "HdrUnavailable" : "HdrTargetUnavailable");
        bool enable = DesiredState(mode, monitors);
        int firstError = 0;
        foreach (var monitor in monitors)
        {
            if (monitor.Enabled == enable)
                continue;
            var target = monitor.Target;
            var state = new SetColor
            {
                Header = MakeHeader(target.Modern ? SetHdr : SetAdvancedColor, sizeof(SetColor),
                    new AdapterId { Low = target.Low, High = target.High }, target.Id),
                Enabled = enable ? 1u : 0u
            };
            int status = DisplayConfigSetDeviceInfo(&state);
            if (status != 0 && firstError == 0)
                firstError = status;
        }
        if (firstError == 5)
            throw new RestUnavailableException("HdrAccessDenied");
        Check(firstError);
    }

    private static PathInfo[] ActivePaths()
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            Check(GetDisplayConfigBufferSizes(OnlyActivePaths, out uint paths, out uint modes));
            var pathInfo = new PathInfo[paths];
            var modeInfo = new ModeInfo[modes];
            fixed (PathInfo* p = pathInfo)
            fixed (ModeInfo* m = modeInfo)
            {
                int status = QueryDisplayConfig(OnlyActivePaths, ref paths, p, ref modes, m, null);
                if (status == InsufficientBuffer)
                    continue;
                Check(status);
            }
            Array.Resize(ref pathInfo, (int)paths);
            return pathInfo;
        }
        throw new Win32Exception(InsufficientBuffer);
    }

    private static Header MakeHeader(int type, int size, AdapterId adapter, uint id) =>
        new() { Type = type, Size = (uint)size, Adapter = adapter, Id = id };

    private static void Check(int result)
    {
        if (result != 0)
            throw new Win32Exception(result);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdapterId { internal uint Low; internal int High; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Header { internal int Type; internal uint Size; internal AdapterId Adapter; internal uint Id; }
    [StructLayout(LayoutKind.Sequential)]
    private struct SourceInfo { internal AdapterId Adapter; internal uint Id, ModeIndex, Status; }
    [StructLayout(LayoutKind.Sequential)]
    private struct TargetInfo
    {
        internal AdapterId Adapter;
        internal uint Id, ModeIndex, Technology, Rotation, Scaling, RefreshNumerator, RefreshDenominator, Scanline, Available, Status;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct PathInfo { internal SourceInfo Source; internal TargetInfo Target; internal uint Flags; }
    [StructLayout(LayoutKind.Sequential, Size = 64)]
    private struct ModeInfo { internal ulong Alignment; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ColorInfo { internal Header Header; internal uint Flags, Encoding, BitsPerChannel, Mode; }
    [StructLayout(LayoutKind.Sequential)]
    private struct SetColor { internal Header Header; internal uint Enabled; }
    [StructLayout(LayoutKind.Sequential)]
    private struct TargetName
    {
        internal Header Header;
        internal uint Flags, Technology;
        internal ushort EdidManufacturer, EdidProduct;
        internal uint Connector;
        internal fixed char FriendlyName[64];
        internal fixed char DevicePath[128];
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint paths, PathInfo* pathInfo, ref uint modes, ModeInfo* modeInfo, uint* topology);
    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(void* packet);
    [DllImport("user32.dll")]
    private static extern int DisplayConfigSetDeviceInfo(void* packet);
}
