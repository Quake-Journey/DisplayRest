using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace DisplayRest;

[Flags]
internal enum Modifiers
{
    None = 0,
    Ctrl = 1,
    Shift = 2,
    Alt = 4,
    Win = 8
}

internal sealed record Shortcut(int Key, Modifiers Modifiers,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] GamepadButtons Gamepad = GamepadButtons.None)
{
    internal const int Pause = 0x13;
    internal const int ScrollLock = 0x91;
    internal const int ControlBreak = 0x03;

    internal bool IsGamepad => Gamepad != GamepadButtons.None;

    internal bool IsValid => IsGamepad
        ? Key == 0 && Modifiers == Modifiers.None && (Gamepad & ~GamepadButtons.Supported) == 0
        : Key is >= 0 and <= 254 &&
        (Key != 0 || Modifiers != Modifiers.None) &&
        (Modifiers & ~(Modifiers.Ctrl | Modifiers.Shift | Modifiers.Alt | Modifiers.Win)) == 0;

    internal static ReadOnlySpan<int> ModifierKeys => [0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C];

    internal static Modifiers ModifierFor(int key) => key switch
    {
        0x11 or 0xA2 or 0xA3 => Modifiers.Ctrl,
        0x10 or 0xA0 or 0xA1 => Modifiers.Shift,
        0x12 or 0xA4 or 0xA5 => Modifiers.Alt,
        0x5B or 0x5C => Modifiers.Win,
        _ => Modifiers.None
    };

    public override string ToString()
    {
        if (IsGamepad)
            return GamepadLabels.Format(Gamepad);

        var keys = new List<string>();
        if (Modifiers.HasFlag(Modifiers.Ctrl)) keys.Add("Ctrl");
        if (Modifiers.HasFlag(Modifiers.Shift)) keys.Add("Shift");
        if (Modifiers.HasFlag(Modifiers.Alt)) keys.Add("Alt");
        if (Modifiers.HasFlag(Modifiers.Win)) keys.Add("Win");
        if (Key != 0) keys.Add(KeyName(Key));
        return string.Join(" + ", keys);
    }

    private static string KeyName(int key)
    {
        if (key is >= 0x70 and <= 0x87)
            return $"F{key - 0x6F}";
        if (key is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A)
            return ((char)key).ToString();
        if (key is >= 0x60 and <= 0x69)
            return $"Num {key - 0x60}";

        return key switch
        {
            Pause => "Pause",
            ScrollLock => "Scroll Lock",
            ControlBreak => "Break",
            0x1B => "Esc",
            0x20 => "Space",
            0x2C => "Print Screen",
            0x90 => "Num Lock",
            _ => WindowsKeyName(key)
        };
    }

    private static string WindowsKeyName(int key)
    {
        uint scan = WindowsNative.MapVirtualKey((uint)key, 4); // MAPVK_VK_TO_VSC_EX
        int parameter = (int)((scan & 0xFF) << 16);
        if ((scan & 0xFF00) == 0xE000)
            parameter |= 1 << 24;

        var label = new StringBuilder(80);
        return WindowsNative.GetKeyNameText(parameter, label, label.Capacity) > 0 ? label.ToString() : $"VK {key:X2}";
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<LanguagePreference>))]
internal enum LanguagePreference { System, Russian, English }

internal sealed record UserSettings(Shortcut? Shortcut = null, bool WakeByMouse = true, bool WakeByKeyboard = true,
    LanguagePreference Language = LanguagePreference.System, bool CloseToTray = true, bool StartInTray = false,
    bool AutoUpdate = false, Shortcut? SleepShortcut = null, Shortcut? HdrShortcut = null,
    Shortcut? GamepadShortcut = null, string? HdrTarget = null, ulong GamepadTarget = 0,
    RestPreset[]? Presets = null)
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "DisplayRest";
    internal static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DisplayRest");
    private static readonly string SettingsFile = Path.Combine(Folder, "settings.json");

    internal static UserSettings Load()
    {
        if (!File.Exists(SettingsFile))
            return new();

        var settings = JsonSerializer.Deserialize(File.ReadAllText(SettingsFile), SettingsJson.Default.UserSettings) ??
            throw new InvalidDataException("Файл параметров пуст.");
        if (settings.Bindings().Any(binding => !binding.Key.IsValid) ||
            (settings.Shortcut is null && !settings.WakeByMouse && !settings.WakeByKeyboard))
            throw new InvalidDataException("В файле параметров указана недопустимая комбинация пробуждения.");
        if (settings.PresetList.Length > RestPreset.Limit ||
            settings.PresetList.Any(preset => preset is null || !preset.IsValid()) ||
            settings.PresetList.Select(preset => preset.Id).Distinct().Count() != settings.PresetList.Length ||
            settings.Bindings().GroupBy(binding => binding.Key).Any(group => group.Count() > 1))
            throw new InvalidDataException("Файл параметров содержит недопустимый пресет или повторяющуюся горячую клавишу.");
        return Enum.IsDefined(settings.Language) ? settings : settings with { Language = LanguagePreference.System };
    }

    [JsonIgnore]
    internal RestPreset[] PresetList => Presets ?? [];

    internal RestPreset? FindPreset(Guid id) => Array.Find(PresetList, preset => preset.Id == id);

    internal Shortcut? ShortcutFor(CommandTarget target) => target switch
    {
        CommandTarget.Displays => Shortcut,
        CommandTarget.Sleep => SleepShortcut,
        CommandTarget.Hdr => HdrShortcut,
        CommandTarget.Gamepads => GamepadShortcut,
        _ => null
    };

    internal UserSettings SetShortcut(CommandTarget target, Shortcut? key) => target switch
    {
        CommandTarget.Displays => this with { Shortcut = key },
        CommandTarget.Sleep => this with { SleepShortcut = key },
        CommandTarget.Hdr => this with { HdrShortcut = key },
        CommandTarget.Gamepads => this with { GamepadShortcut = key },
        _ => throw new ArgumentOutOfRangeException(nameof(target))
    };

    internal IEnumerable<(Shortcut Key, CommandId Command)> Bindings()
    {
        if (Shortcut is { } displays) yield return (displays, new(CommandTarget.Displays));
        if (SleepShortcut is { } sleep) yield return (sleep, new(CommandTarget.Sleep));
        if (HdrShortcut is { } hdr) yield return (hdr, new(CommandTarget.Hdr));
        if (GamepadShortcut is { } gamepad) yield return (gamepad, new(CommandTarget.Gamepads));
        foreach (var preset in PresetList)
            if (preset?.Shortcut is { } key)
                yield return (key, new(CommandTarget.Preset, preset.Id));
    }

    internal CommandId? Conflict(Shortcut key, CommandId owner)
    {
        foreach (var (bound, command) in Bindings())
            if (bound == key && command != owner)
                return command;
        return null;
    }

    internal void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
        var tempPath = SettingsFile + ".tmp";
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(this, SettingsJson.Default.UserSettings));
            File.Move(tempPath, SettingsFile, overwrite: true);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    internal static string StartupCommand => $"\"{Environment.ProcessPath}\" --tray";

    internal static string? RegisteredStartup()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunValue) as string;
    }

    internal static void SetStartup(bool enable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enable)
            key.SetValue(RunValue, StartupCommand, RegistryValueKind.String);
        else
            key.DeleteValue(RunValue, throwOnMissingValue: false);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(UserSettings))]
internal partial class SettingsJson : JsonSerializerContext;
