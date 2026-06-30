using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using MonitorInformation.Models;

namespace MonitorInformation.Services;

public sealed class DuckDuckGoPanelSearchProvider : ISpecProvider
{
    private static readonly HttpClient HttpClient = CreateHttpClient();

    public string Name => "Panelook via DuckDuckGo";

    public async Task<OnlineSpecResult?> SearchAsync(MonitorIdentity identity, CancellationToken cancellationToken)
    {
        var query = PanelookSpecProvider.BuildPanelQuery(identity);
        if (query.Length == 0)
        {
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        var searchUrl = $"https://duckduckgo.com/html/?q={Uri.EscapeDataString(query + " Panelook")}";
        var html = await HttpClient.GetStringAsync(searchUrl, timeout.Token).ConfigureAwait(false);
        var result = FindBestResult(query, html);
        return result is null ? null : CreateResult(query, result.Value);
    }

    private static SearchResult? FindBestResult(string query, string html)
    {
        var matches = Regex.Matches(
            html,
            "(?s)<a[^>]+class=\"result__a\"[^>]+href=\"(?<url>[^\"]+)\"[^>]*>(?<title>.*?)</a>.*?<a[^>]+class=\"result__snippet\"[^>]*>(?<snippet>.*?)</a>",
            RegexOptions.IgnoreCase);

        SearchResult? best = null;
        var bestScore = 0;
        foreach (Match match in matches)
        {
            var title = Clean(match.Groups["title"].Value);
            var snippet = Clean(match.Groups["snippet"].Value);
            var url = DecodeDuckDuckGoUrl(WebUtility.HtmlDecode(match.Groups["url"].Value));
            var haystack = $"{title} {snippet}";
            if (!haystack.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var score = 35;
            if (url.Contains("panelook.com", StringComparison.OrdinalIgnoreCase))
            {
                score += 25;
            }

            if (title.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                score += 15;
            }

            if (snippet.Contains("Product Code", StringComparison.OrdinalIgnoreCase) ||
                snippet.Contains("display panel", StringComparison.OrdinalIgnoreCase))
            {
                score += 10;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = new SearchResult(title, snippet, url, Math.Min(score, 85));
            }
        }

        return bestScore >= 55 ? best : null;
    }

    private static OnlineSpecResult CreateResult(string query, SearchResult searchResult)
    {
        var fields = new List<OnlineSpecField>();
        Add(fields, "Model", query);
        AddRegex(fields, "Product code", searchResult.Snippet, "Product Code:\\s*([A-Z0-9\\-]+)");
        AddRegex(fields, "Manufacturer", searchResult.Snippet, "from\\s+([^,.]+)");
        AddRegex(fields, "Size", searchResult.Snippet, "(\\d{2}(?:\\.\\d)?\\s*(?:inch|\\\"))");
        AddRegex(fields, "Panel type", searchResult.Snippet, "\\b(AM-OLED|OLED|IPS(?: LCD)?|VA(?: LCD)?|TN(?: LCD)?|TFT-LCD|LCD)\\b");
        AddRegex(fields, "Resolution", searchResult.Snippet, "(\\d{3,5}\\s*(?:\\(RGB\\))?\\s*[xX×]\\s*\\d{3,5})");
        AddRegex(fields, "Color / bit depth", searchResult.Snippet, "((?:DCI-P3|sRGB|Adobe RGB|\\d+\\s*bit)[^.]*)");

        return new OnlineSpecResult
        {
            ProviderName = "Panelook via DuckDuckGo",
            SourceUrl = searchResult.Url.Length == 0 ? $"https://duckduckgo.com/html/?q={Uri.EscapeDataString(query + " Panelook")}" : searchResult.Url,
            RetrievedAt = DateTimeOffset.UtcNow,
            Confidence = searchResult.Confidence,
            MatchSummary = PanelookParser.CleanPanelookTitle(searchResult.Title, query),
            Fields = fields
        };
    }

    private static string DecodeDuckDuckGoUrl(string url)
    {
        if (!url.Contains("uddg=", StringComparison.OrdinalIgnoreCase))
        {
            return url.StartsWith("//", StringComparison.Ordinal) ? $"https:{url}" : url;
        }

        var match = Regex.Match(url, "[?&]uddg=(?<url>[^&]+)", RegexOptions.IgnoreCase);
        return match.Success ? Uri.UnescapeDataString(match.Groups["url"].Value) : url;
    }

    private static string Clean(string html)
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

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 MonitorInformation/0.2.1");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        return client;
    }

    private readonly record struct SearchResult(string Title, string Snippet, string Url, int Confidence);
}
