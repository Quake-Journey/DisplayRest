using System;
using System.Globalization;

namespace DisplayRest;

internal static partial class UiText
{
    private static bool _russian;

    internal static void Use(LanguagePreference preference)
    {
        _russian = preference switch
        {
            LanguagePreference.Russian => true,
            LanguagePreference.English => false,
            _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru"
        };
    }

    internal static string Get(string key, object? value = null)
    {
        var text = (_russian ? RU(key) : EN(key)) ?? key;
        return value is null ? text : string.Format(CultureInfo.CurrentCulture, text, value);
    }
}
