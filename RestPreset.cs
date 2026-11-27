using System;
using System.Collections.Generic;

namespace DisplayRest;

[Flags]
internal enum RestAction { None = 0, Displays = 1, Sleep = 2, Hdr = 4, Gamepads = 8, Lock = 16, BlackScreen = 32 }
internal enum HdrMode { Toggle, On, Off }
internal enum CommandTarget { Displays, Sleep, Hdr, Gamepads, Preset, BlackScreen }

internal readonly record struct CommandId(CommandTarget Target, Guid Preset = default)
{
    internal string Label(UserSettings settings) => Target switch
    {
        CommandTarget.Displays => UiText.Get("DisplayHeading"),
        CommandTarget.Sleep => UiText.Get("PcSleep"),
        CommandTarget.Hdr => "HDR",
        CommandTarget.Gamepads => UiText.Get("GamepadDisconnect"),
        CommandTarget.BlackScreen => UiText.Get("BlackScreen"),
        _ => settings.FindPreset(Preset)?.Name ?? UiText.Get("TabPresets")
    };
}

internal sealed record RestPreset(Guid Id, string Name, string Description, RestAction Actions, HdrMode HdrMode, Shortcut? Shortcut)
{
    internal const int Limit = 10;
    internal const int MaxName = 48;
    internal const int MaxDescription = 240;
    private const RestAction All = RestAction.Displays | RestAction.Sleep | RestAction.Hdr | RestAction.Gamepads | RestAction.Lock | RestAction.BlackScreen;

    internal bool IsValid() => Id != Guid.Empty && !string.IsNullOrWhiteSpace(Name) && Name.Length <= MaxName &&
        Description is { Length: <= MaxDescription } && Actions != RestAction.None && (Actions & ~All) == 0 &&
        Enum.IsDefined(HdrMode) && Shortcut is not { IsValid: false };
}

internal sealed record RestRequest(RestAction Actions, HdrMode HdrMode = HdrMode.Toggle)
{
    private static readonly RestAction[] Order = [RestAction.Gamepads, RestAction.Hdr, RestAction.BlackScreen, RestAction.Lock, RestAction.Displays, RestAction.Sleep];

    internal IEnumerable<RestAction> Steps()
    {
        foreach (var action in Order)
            if ((Actions & action) != 0)
                yield return action;
    }
}
