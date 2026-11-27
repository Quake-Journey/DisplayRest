using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DisplayRest;

internal sealed record ScreenSaver(bool Enabled, uint Seconds, bool EnableManaged, bool TimeoutManaged)
{
    private const string Desktop = @"Control Panel\Desktop";
    private const string Policy = @"Software\Policies\Microsoft\Windows\Control Panel\Desktop";
    private const string SaverFile = "SCRNSAVE.EXE";

    internal static ScreenSaver Read()
    {
        using var policy = Registry.CurrentUser.OpenSubKey(Policy);
        using var desktop = Registry.CurrentUser.OpenSubKey(Desktop);
        bool selected = !string.IsNullOrWhiteSpace((policy?.GetValue(SaverFile) ?? desktop?.GetValue(SaverFile)) as string);
        return new(ReadParameter(0x10) != 0 && selected, ReadParameter(0x0E),
            policy?.GetValue("ScreenSaveActive") is not null, policy?.GetValue("ScreenSaveTimeOut") is not null);
    }

    internal static void SetEnabled(bool enabled)
    {
        var current = Read();
        if (current.EnableManaged)
            throw new RestUnavailableException("SaverManaged");
        if (enabled && current.Seconds == 0)
            throw new RestUnavailableException("SaverInvalid");
        using var policy = Registry.CurrentUser.OpenSubKey(Policy);
        using var desktop = Registry.CurrentUser.OpenSubKey(Desktop, writable: enabled);
        object? previous = desktop?.GetValue(SaverFile, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        bool chooseBlank = enabled && policy?.GetValue(SaverFile) is null && string.IsNullOrWhiteSpace(previous as string);
        var blank = Path.Combine(Environment.SystemDirectory, "scrnsave.scr");
        if (chooseBlank)
        {
            if (desktop is null || !File.Exists(blank))
                throw new RestUnavailableException("SaverSelect");
            desktop.SetValue(SaverFile, blank, RegistryValueKind.String);
        }
        try
        {
            WriteParameter(0x11, enabled ? 1u : 0u);
        }
        catch (Win32Exception)
        {
            if (chooseBlank && Equals(desktop!.GetValue(SaverFile), blank))
            {
                if (previous is null) desktop.DeleteValue(SaverFile, false);
                else desktop.SetValue(SaverFile, previous);
            }
            throw;
        }
    }

    internal static void SetTimeout(uint seconds)
    {
        if (seconds is 0 or > int.MaxValue)
            throw new RestUnavailableException("SaverInvalid");
        if (Read().TimeoutManaged)
            throw new RestUnavailableException("SaverManaged");
        WriteParameter(0x0F, seconds);
    }

    private static uint ReadParameter(uint action) =>
        GetParameter(action, 0, out uint value, 0) ? value : throw new Win32Exception(Marshal.GetLastWin32Error());

    private static void WriteParameter(uint action, uint value)
    {
        // Persist to the user profile and broadcast WM_SETTINGCHANGE.
        if (!SetParameter(action, value, 0, 3))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    private static extern bool GetParameter(uint action, uint param, out uint data, uint flags);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    private static extern bool SetParameter(uint action, uint param, nint data, uint flags);
}
