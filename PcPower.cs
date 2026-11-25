using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DisplayRest;

internal static class PcPower
{
    internal static bool CanSleep => IsPwrSuspendAllowed();

    internal static void Sleep()
    {
        if (!CanSleep)
            throw new RestUnavailableException("PcSleepUnavailable");

        if (!OpenProcessToken(GetCurrentProcess(), 0x28, out var token))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        using (token)
        {
            if (!LookupPrivilegeValue(null, "SeShutdownPrivilege", out var privilege))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            var requested = new TokenPrivileges { Count = 1, Privilege = privilege, Attributes = 2 };
            var success = AdjustTokenPrivileges(token, false, in requested, Marshal.SizeOf<TokenPrivileges>(), out var previous, out _);
            int error = Marshal.GetLastPInvokeError();
            if (!success || error != 0)
                throw new Win32Exception(error);
            try
            {
                if (!SetSuspendState(false, false, false))
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
            finally
            {
                RestoreTokenPrivileges(token, false, in previous, 0, 0, 0);
            }
        }
    }

    internal static void Lock()
    {
        if (!LockWorkStation())
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid { internal uint Low; internal int High; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges { internal uint Count; internal Luid Privilege; internal uint Attributes; }

    [DllImport("powrprof.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool IsPwrSuspendAllowed();

    [DllImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SetSuspendState([MarshalAs(UnmanagedType.U1)] bool hibernate,
        [MarshalAs(UnmanagedType.U1)] bool force, [MarshalAs(UnmanagedType.U1)] bool disableWakeEvents);

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(nint process, uint access, out SafeFileHandle token);

    [DllImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool LookupPrivilegeValue(string? system, string name, out Luid luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(SafeFileHandle token, bool disableAll, in TokenPrivileges requested,
        int size, out TokenPrivileges previous, out int returnedSize);

    [DllImport("advapi32.dll", EntryPoint = "AdjustTokenPrivileges")]
    private static extern bool RestoreTokenPrivileges(SafeFileHandle token, bool disableAll, in TokenPrivileges previous,
        int size, nint unused, nint returnedSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool LockWorkStation();
}

internal sealed class RestUnavailableException(string key) : Exception
{
    internal string Key { get; } = key;
}
