using System.Net;
using System.Text.RegularExpressions;
using MonitorInformation.Models;

namespace MonitorInformation.Services;

public static class PanelookParser
{
    public static OnlineSpecResult? CreateResultFromHtml(
        string query,
        string sourceUrl,
        string providerName,
        string html,
        int confidence)
    {
        if (string.IsNullOrWhiteSpace(html) ||
            !html.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            IsVerificationPage(html))
        {
            return null;
        }

        var text = ToPlainText(html);
        var description = ExtractMetaDescription(html);
        var combined = $"{description} {text}";
        var fields = new List<OnlineSpecField>();

        Add(fields, "Model", query);
        AddRegex(fields, "Manufacturer", combined, "(?:product from|from)\\s+([^,.()]+)");
        AddRegex(fields, "Product code", combined, "Product Code:\\s*([A-Z0-9\\-]+)");
        AddRegex(fields, "Panel type", combined, "\\b(AM-OLED|OLED|IPS(?: LCD)?|VA(?: LCD)?|TN(?: LCD)?|TFT-LCD|LCD)\\b");
        AddRegex(fields, "Size", combined, "(\\d{2}(?:\\.\\d)?\\s*(?:inch|\\\"|'))");
        AddRegex(fields, "Resolution", combined, "(\\d{3,5}\\s*(?:\\(RGB\\))?\\s*[xX×]\\s*\\d{3,5})");
        AddRegex(fields, "Brightness", combined, "(\\d{2,4}\\s*(?:cd/m²|nit))");
        AddRegex(fields, "Refresh rate", combined, "(\\d{2,3}\\s*Hz)");
        AddRegex(fields, "Colors", combined, "(\\d+(?:\\.\\d+)?[BM]\\s*color|\\d+\\s*bit|100%\\s*(?:DCI-P3|sRGB))");

        AddDefinition(fields, html, "Active Area");
        AddDefinition(fields, html, "Physical Size");
        AddDefinition(fields, html, "Surface Coating");
        AddDefinition(fields, html, "Luminance");
        AddDefinition(fields, html, "Contrast Ratio");
        AddDefinition(fields, html, "Response Time");
        AddDefinition(fields, html, "Support Color");
        AddDefinition(fields, html, "Touch Panel");
        AddDefinition(fields, html, "Interface Type");
        AddDefinition(fields, html, "Power Supply");
        AddDefinition(fields, html, "Environment");

        return new OnlineSpecResult
        {
            ProviderName = providerName,
            SourceUrl = sourceUrl,
            RetrievedAt = DateTimeOffset.UtcNow,
            Confidence = confidence,
            MatchSummary = query,
            Fields = Deduplicate(fields)
        };
    }

    public static string CleanPanelookTitle(string title, string fallback)
    {
        var cleaned = title;
        cleaned = Regex.Replace(cleaned, "\\s+-\\s+Panelook\\.com\\s*$", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, "\\s+(Overview|Specification.*|Datasheets?)\\s*$", "", RegexOptions.IgnoreCase);
        var modelMatch = Regex.Match(cleaned, "([A-Z0-9]+(?:-[A-Z0-9]+)+)", RegexOptions.IgnoreCase);
        return modelMatch.Success ? modelMatch.Groups[1].Value : fallback;
    }

    public static bool IsVerificationPage(string html)
    {
        return html.Contains("Please verify yourself", StringComparison.OrdinalIgnoreCase) ||
               html.Contains("Please slide to verify", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractMetaDescription(string html)
    {
        var match = Regex.Match(
            html,
            "<meta\\s+(?:name|property)=[\"'](?:description|og:description)[\"']\\s+content=[\"'](?<value>.*?)[\"']",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        return match.Success ? WebUtility.HtmlDecode(match.Groups["value"].Value) : "";
    }

    private static void AddDefinition(List<OnlineSpecField> fields, string html, string name)
    {
        var pattern = $"<dt[^>]*>\\s*{Regex.Escape(name)}\\s*</dt>\\s*<dd[^>]*>(?<value>.*?)</dd>";
        var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!match.Success)
        {
            return;
        }

        Add(fields, name, ToPlainText(match.Groups["value"].Value));
    }

    private static string ToPlainText(string html)
    {
        var text = Regex.Replace(html, "<[^>]+>", " ");
        text = WebUtility.HtmlDecode(text);
        return Regex.Replace(text, "\\s+", " ").Trim();
    }

    private static void Add(List<OnlineSpecField> fields, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            fields.Add(new OnlineSpecField { Name = name, Value = value.Trim() });
        }
    }

    private static void AddRegex(List<OnlineSpecField> fields, string name, string text, string pattern)
    {
        var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
        if (match.Success)
        {
            Add(fields, name, match.Groups[1].Value);
        }
    }

    private static IReadOnlyList<OnlineSpecField> Deduplicate(List<OnlineSpecField> fields)
    {
        var result = new List<OnlineSpecField>(fields.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields)
        {
            var key = $"{field.Name}|{field.Value}";
            if (seen.Add(key))
            {
                result.Add(field);
            }
        }

        return result;
    }
}
