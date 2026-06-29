using System.Globalization;
using System.IO;
using System.Text.Json;
using MonitorInformation.Models;

namespace MonitorInformation.Services;

public sealed class AppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public AppSettingsService()
    {
        _settingsPath = Path.Combine(AppContext.BaseDirectory, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return CreateDefault();
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath), JsonOptions) ?? CreateDefault();
            if (string.IsNullOrWhiteSpace(settings.Language))
            {
                settings.Language = LocalizationService.MatchSupportedCulture(CultureInfo.CurrentUICulture.Name);
            }

            if (!ThemeService.IsKnownTheme(settings.Theme))
            {
                settings.Theme = ThemeService.SystemTheme;
            }

            return settings;
        }
        catch
        {
            return CreateDefault();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch
        {
            // Portable builds may be placed in read-only directories. Settings are best effort.
        }
    }

    private static AppSettings CreateDefault()
    {
        return new AppSettings
        {
            Language = LocalizationService.MatchSupportedCulture(CultureInfo.CurrentUICulture.Name),
            Theme = ThemeService.SystemTheme
        };
    }
}
