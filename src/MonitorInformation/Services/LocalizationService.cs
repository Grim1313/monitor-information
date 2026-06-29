using System.Globalization;
using System.IO;
using System.Text.Json;

namespace MonitorInformation.Services;

public sealed class LocalizationService
{
    public static readonly string[] SupportedCultures = ["en-US", "ru-RU", "es-ES"];

    private Dictionary<string, string> _fallback = [];
    private Dictionary<string, string> _current = [];

    public LocalizationService()
    {
        _fallback = LoadLanguage("en-US");
        _current = _fallback;
    }

    public void SetCulture(string cultureName)
    {
        var culture = MatchSupportedCulture(cultureName);
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);

        _fallback = LoadLanguage("en-US");
        _current = culture == "en-US" ? _fallback : LoadLanguage(culture);
    }

    public string Text(string key)
    {
        if (_current.TryGetValue(key, out var value))
        {
            return value;
        }

        if (_fallback.TryGetValue(key, out value))
        {
            return value;
        }

        return key;
    }

    public string Format(string key, IReadOnlyDictionary<string, string> values)
    {
        var text = Text(key);
        foreach (var pair in values)
        {
            text = text.Replace("{" + pair.Key + "}", pair.Value, StringComparison.Ordinal);
        }

        return text;
    }

    public static string MatchSupportedCulture(string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName))
        {
            return "en-US";
        }

        if (SupportedCultures.Contains(cultureName, StringComparer.OrdinalIgnoreCase))
        {
            return SupportedCultures.First(c => string.Equals(c, cultureName, StringComparison.OrdinalIgnoreCase));
        }

        var prefix = cultureName.Split('-', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return prefix?.ToLowerInvariant() switch
        {
            "ru" => "ru-RU",
            "es" => "es-ES",
            _ => "en-US"
        };
    }

    private static Dictionary<string, string> LoadLanguage(string culture)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "resources", "i18n", $"{culture}.json");
        if (!File.Exists(path))
        {
            return [];
        }

        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    }
}
