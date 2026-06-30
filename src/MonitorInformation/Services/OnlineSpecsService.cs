using MonitorInformation.Models;

namespace MonitorInformation.Services;

public sealed class OnlineSpecsService
{
    private readonly IReadOnlyList<ISpecProvider> _providers;
    private readonly OnlineSpecCache _cache = new();

    public OnlineSpecsService()
    {
        _providers =
        [
            new PanelookSpecProvider(),
            new YandexCachedPanelookProvider(),
            new DuckDuckGoPanelSearchProvider(),
            new EnergyStarSpecProvider()
        ];
    }

    public async Task<OnlineSpecResult?> SearchAsync(MonitorIdentity identity, CancellationToken cancellationToken, bool forceRefresh = false)
    {
        var cacheKey = OnlineSpecCache.CreateKey(identity);
        if (!forceRefresh)
        {
            var cached = _cache.TryRead(cacheKey);
            if (cached is not null)
            {
                return cached;
            }
        }

        foreach (var provider in _providers)
        {
            var result = await provider.SearchAsync(identity, cancellationToken).ConfigureAwait(false);
            if (result is not null)
            {
                _cache.TryWrite(cacheKey, result);
                return result;
            }
        }

        return null;
    }

    public void ClearCache()
    {
        _cache.ClearAll();
    }
}

public interface ISpecProvider
{
    string Name { get; }

    Task<OnlineSpecResult?> SearchAsync(MonitorIdentity identity, CancellationToken cancellationToken);
}
