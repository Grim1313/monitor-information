using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using MonitorInformation.Models;

namespace MonitorInformation.Services;

public sealed class YandexCachedPanelookProvider : ISpecProvider
{
    private static readonly HttpClient HttpClient = CreateHttpClient();

    public string Name => "Panelook cached by Yandex";

    public async Task<OnlineSpecResult?> SearchAsync(MonitorIdentity identity, CancellationToken cancellationToken)
    {
        var query = PanelookSpecProvider.BuildPanelQuery(identity);
        if (query.Length == 0)
        {
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));

        var searchUrl = $"https://yandex.ru/search/?text={Uri.EscapeDataString(query + " site:panelook.com")}";
        var searchHtml = await GetStringOrEmptyAsync(searchUrl, timeout.Token).ConfigureAwait(false);
        if (searchHtml.Length == 0 ||
            searchHtml.Contains("captcha", StringComparison.OrdinalIgnoreCase) ||
            !searchHtml.Contains("yandbtm", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        foreach (var cacheUrl in ExtractCacheUrls(searchHtml))
        {
            var cachedHtml = await GetStringOrEmptyAsync(cacheUrl, timeout.Token).ConfigureAwait(false);
            var sourceUrl = ExtractOriginalPanelookUrl(cacheUrl, cachedHtml);
            var result = PanelookParser.CreateResultFromHtml(query, sourceUrl, Name, cachedHtml, 82);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    private static IEnumerable<string> ExtractCacheUrls(string html)
    {
        var urls = new List<string>();
        foreach (Match match in Regex.Matches(html, "https?://yandexwebcache\\.net/yandbtm\\?[^\"'<>\\s]+", RegexOptions.IgnoreCase))
        {
            urls.Add(WebUtility.HtmlDecode(match.Value));
        }

        foreach (Match match in Regex.Matches(html, "(?:href|data-counter)=[\"'](?<url>[^\"']*yandbtm\\?[^\"']+)[\"']", RegexOptions.IgnoreCase))
        {
            var url = WebUtility.HtmlDecode(match.Groups["url"].Value);
            if (url.StartsWith("//", StringComparison.Ordinal))
            {
                url = $"https:{url}";
            }
            else if (url.StartsWith("/yandbtm", StringComparison.Ordinal))
            {
                url = $"https://yandexwebcache.net{url}";
            }

            urls.Add(url);
        }

        return urls
            .Where(url => url.Contains("panelook.com", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string ExtractOriginalPanelookUrl(string cacheUrl, string cachedHtml)
    {
        var baseMatch = Regex.Match(cachedHtml, "<base\\s+href=[\"'](?<url>[^\"']+)[\"']", RegexOptions.IgnoreCase);
        if (baseMatch.Success)
        {
            return WebUtility.HtmlDecode(baseMatch.Groups["url"].Value);
        }

        var urlMatch = Regex.Match(cacheUrl, "[?&]url=(?<url>[^&]+)", RegexOptions.IgnoreCase);
        return urlMatch.Success ? Uri.UnescapeDataString(urlMatch.Groups["url"].Value) : cacheUrl;
    }

    private static async Task<string> GetStringOrEmptyAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            return await HttpClient.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return "";
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 MonitorInformation/0.2.1");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        return client;
    }
}
