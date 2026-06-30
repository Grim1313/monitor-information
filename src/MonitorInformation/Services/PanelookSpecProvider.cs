using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using MonitorInformation.Models;

namespace MonitorInformation.Services;

public sealed class PanelookSpecProvider : ISpecProvider
{
    private static readonly HttpClient HttpClient = CreateHttpClient();

    public string Name => "Panelook";

    public async Task<OnlineSpecResult?> SearchAsync(MonitorIdentity identity, CancellationToken cancellationToken)
    {
        var query = BuildPanelQuery(identity);
        if (query.Length == 0)
        {
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        var searchUrl = $"https://www.panelook.com/modelsearch.php?keyword={Uri.EscapeDataString(query)}";
        var html = await GetStringOrEmptyAsync(searchUrl, timeout.Token).ConfigureAwait(false);
        if (PanelookParser.IsVerificationPage(html))
        {
            return null;
        }

        var modelUrl = ExtractFirstPanelookModelUrl(html);
        if (modelUrl.Length == 0)
        {
            return null;
        }

        html = await GetStringOrEmptyAsync(modelUrl, timeout.Token).ConfigureAwait(false);
        if (html.Length == 0 || PanelookParser.IsVerificationPage(html))
        {
            return null;
        }

        return PanelookParser.CreateResultFromHtml(query, modelUrl, Name, html, 70);
    }

    public static string BuildPanelQuery(MonitorIdentity identity)
    {
        var candidates = new[]
        {
            identity.DisplayName,
            BuildEdidPanelKey(identity.ManufacturerId, identity.ProductCodeHex)
        };

        foreach (var candidate in candidates)
        {
            var value = (candidate ?? "").Trim();
            if (LooksLikePanelModel(value))
            {
                return value;
            }
        }

        return "";
    }

    private static string BuildEdidPanelKey(string manufacturerId, string productCodeHex)
    {
        if (manufacturerId.Length != 3 || !productCodeHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        var code = productCodeHex[2..].PadLeft(4, '0');
        return $"{manufacturerId}{code}";
    }

    private static bool LooksLikePanelModel(string value)
    {
        if (value.Length < 6 ||
            value.Contains("generic", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("pnp", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return Regex.IsMatch(value, "^[A-Z0-9][A-Z0-9\\-_.]{5,}$", RegexOptions.IgnoreCase);
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

    private static string ExtractFirstPanelookModelUrl(string html)
    {
        var match = Regex.Match(html, "href=[\"'](?<url>[^\"']+_(?:overview|parameter)_\\d+\\.html)[\"']", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return "";
        }

        var url = WebUtility.HtmlDecode(match.Groups["url"].Value);
        if (url.StartsWith("//", StringComparison.Ordinal))
        {
            return $"https:{url}";
        }

        return url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? url
            : $"https://www.panelook.com/{url.TrimStart('/')}";
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 MonitorInformation/0.1.0");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        return client;
    }
}
