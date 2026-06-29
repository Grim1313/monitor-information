namespace MonitorInformation.Models;

public sealed class AppSettings
{
    public string Language { get; set; } = "";

    public string Theme { get; set; } = Services.ThemeService.SystemTheme;
}
