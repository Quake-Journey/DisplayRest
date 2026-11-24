using System;
using System.Globalization;
using System.Windows;

namespace DisplayRest;

internal static class UiText
{
    private static ResourceDictionary? _strings;
    internal static string LanguageTag { get; private set; } = "";

    internal static void Use(LanguagePreference preference)
    {
        var tag = preference switch
        {
            LanguagePreference.Russian => "ru",
            LanguagePreference.English => "en",
            _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? "ru" : "en"
        };
        if (tag == LanguageTag)
            return;

        var strings = new ResourceDictionary { Source = new Uri($"Strings.{tag}.xaml", UriKind.Relative) };
        var merged = Application.Current.Resources.MergedDictionaries;
        if (_strings is null)
            merged.Add(strings);
        else
            merged[merged.IndexOf(_strings)] = strings;
        _strings = strings;
        LanguageTag = tag;
    }

    internal static string Get(string key, object? value = null)
    {
        var text = (string)_strings![key];
        return value is null ? text : string.Format(CultureInfo.GetCultureInfo(LanguageTag), text, value);
    }
}
