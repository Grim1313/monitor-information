using System.Windows;
using Microsoft.Win32;

namespace MonitorInformation.Services;

public sealed class ThemeService
{
    public const string SystemTheme = "system";
    public const string LightTheme = "light";
    public const string OledDarkTheme = "oled-dark";

    public void Apply(string theme)
    {
        var resolved = theme == SystemTheme ? DetectSystemTheme() : theme;
        var source = resolved == OledDarkTheme ? "Themes/OledDark.xaml" : "Themes/Light.xaml";
        var dictionaries = Application.Current.Resources.MergedDictionaries;

        for (var i = dictionaries.Count - 1; i >= 0; i--)
        {
            var uri = dictionaries[i].Source?.OriginalString;
            if (uri is not null && uri.StartsWith("Themes/", StringComparison.OrdinalIgnoreCase))
            {
                dictionaries.RemoveAt(i);
            }
        }

        dictionaries.Add(new ResourceDictionary { Source = new Uri(source, UriKind.Relative) });
    }

    public static bool IsKnownTheme(string? theme)
    {
        return theme is SystemTheme or LightTheme or OledDarkTheme;
    }

    private static string DetectSystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int intValue && intValue == 0 ? OledDarkTheme : LightTheme;
        }
        catch
        {
            return LightTheme;
        }
    }
}
