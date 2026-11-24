using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace DisplayRest;

internal sealed class PowerPlanChangeException(string resourceKey) : InvalidOperationException
{
    internal string ResourceKey { get; } = resourceKey;
}

internal sealed record PowerPlan(Guid Id, string Name, uint Ac, uint Dc)
{
    private static readonly Guid DisplaySubgroup = new("7516B95F-F776-4464-8C53-06167F40CC99");
    private static readonly Guid DisplayTimeout = new("3C0BC021-C8A8-4E07-A973-6B14CBCB2B7E");

    internal static PowerPlan Read()
    {
        var id = ActiveId();
        Check(PowerReadACValueIndex(0, in id, in DisplaySubgroup, in DisplayTimeout, out uint ac));
        Check(PowerReadDCValueIndex(0, in id, in DisplaySubgroup, in DisplayTimeout, out uint dc));
        return new(id, ReadName(id), ac, dc);
    }

    internal void Apply(uint ac, uint dc)
    {
        var live = Read();
        if (live != this)
            throw new PowerPlanChangeException("TimerChanged");

        var id = Id;
        bool acWritten = false;
        bool dcWritten = false;
        try
        {
            if (ac != Ac)
            {
                Check(PowerWriteACValueIndex(0, in id, in DisplaySubgroup, in DisplayTimeout, ac));
                acWritten = true;
            }
            if (dc != Dc)
            {
                Check(PowerWriteDCValueIndex(0, in id, in DisplaySubgroup, in DisplayTimeout, dc));
                dcWritten = true;
            }
            if (ActiveId() != Id)
                throw new PowerPlanChangeException("TimerPlanSwitched");

            Check(PowerSetActiveScheme(0, in id));
        }
        catch
        {
            uint acRollback = acWritten ? PowerWriteACValueIndex(0, in id, in DisplaySubgroup, in DisplayTimeout, Ac) : 0;
            uint dcRollback = dcWritten ? PowerWriteDCValueIndex(0, in id, in DisplaySubgroup, in DisplayTimeout, Dc) : 0;
            if (acRollback != 0 || dcRollback != 0)
                throw new PowerPlanChangeException("TimerRollbackFailed");
            throw;
        }
    }

    internal static string Format(uint seconds) =>
        seconds % 60 == 0 ? (seconds / 60).ToString(CultureInfo.InvariantCulture) :
        $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}";

    internal static bool TryParse(string text, out uint seconds)
    {
        seconds = 0;
        var parts = text.Trim().Split(':');
        if (parts.Length == 1)
        {
            if (!uint.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out uint minutes) ||
                minutes > uint.MaxValue / 60)
                return false;
            seconds = minutes * 60;
            return true;
        }
        if (parts.Length != 3 ||
            !uint.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out uint hour) ||
            !uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out uint minute) || minute >= 60 ||
            !uint.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out uint second) || second >= 60)
            return false;

        ulong value = hour * 3600UL + minute * 60UL + second;
        if (value > uint.MaxValue)
            return false;
        seconds = (uint)value;
        return true;
    }

    private static Guid ActiveId()
    {
        Check(PowerGetActiveScheme(0, out nint address));
        try { return Marshal.PtrToStructure<Guid>(address); }
        finally { LocalFree(address); }
    }

    private static string ReadName(Guid id)
    {
        uint size = 0;
        PowerReadFriendlyName(0, in id, 0, 0, null, ref size);
        if (size == 0)
            return id.ToString();
        var buffer = new byte[size];
        return PowerReadFriendlyName(0, in id, 0, 0, buffer, ref size) == 0
            ? Encoding.Unicode.GetString(buffer, 0, (int)size).TrimEnd('\0')
            : id.ToString();
    }

    private static void Check(uint error)
    {
        if (error != 0)
            throw new Win32Exception((int)error);
    }

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(nint root, out nint activeScheme);

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint memory);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadFriendlyName(nint root, in Guid scheme, nint group, nint setting, [Out] byte[]? bytes, ref uint size);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadACValueIndex(nint root, in Guid scheme, in Guid group, in Guid setting, out uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadDCValueIndex(nint root, in Guid scheme, in Guid group, in Guid setting, out uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteACValueIndex(nint root, in Guid scheme, in Guid group, in Guid setting, uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteDCValueIndex(nint root, in Guid scheme, in Guid group, in Guid setting, uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(nint root, in Guid scheme);
}
