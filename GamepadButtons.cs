using System;
using System.Collections.Generic;
using System.Numerics;

namespace DisplayRest;

[Flags]
internal enum GamepadButtons : ulong
{
    None = 0,
    Up = 1UL << 0,
    Right = 1UL << 1,
    Down = 1UL << 2,
    Left = 1UL << 3,
    Start = 1UL << 4,
    Back = 1UL << 5,
    A = 1UL << 6,
    B = 1UL << 7,
    X = 1UL << 8,
    Y = 1UL << 9,
    LeftBumper = 1UL << 10,
    RightBumper = 1UL << 11,
    LeftTrigger = 1UL << 12,
    RightTrigger = 1UL << 13,
    LeftStick = 1UL << 14,
    RightStick = 1UL << 15,
    Guide = 1UL << 16,
    Touchpad = 1UL << 17,
    Mute = 1UL << 18,
    Supported = ((1UL << 19) - 1) | ((ulong)uint.MaxValue << GamepadLabels.GenericStart)
}

internal static class GamepadLabels
{
    internal const int GenericStart = 24;

    private static readonly string[] Standard =
    [
        "D-pad ↑", "D-pad →", "D-pad ↓", "D-pad ←", "Start", "Back", "A/×", "B/○", "X/□", "Y/△",
        "LB/L1", "RB/R1", "LT/L2", "RT/R2", "LS/L3", "RS/R3", "Guide/PS", "Touchpad", "Mute"
    ];

    internal static string Format(GamepadButtons buttons)
    {
        var labels = new List<string>();
        var remaining = (ulong)buttons;
        while (remaining != 0)
        {
            int index = BitOperations.TrailingZeroCount(remaining);
            if (index < Standard.Length)
                labels.Add(Standard[index]);
            else if (index >= GenericStart)
                labels.Add(UiText.Get("GamepadButtonNumber", index - GenericStart + 1));
            remaining &= remaining - 1;
        }
        return UiText.Get("GamepadBinding", string.Join(" + ", labels));
    }
}
