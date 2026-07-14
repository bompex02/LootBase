using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using LootBase.Application.Abstractions.Pricing;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace LootBase.Infrastructure.Pricing;

public sealed class PricingProvider(
    HttpClient httpClient,
    IDistributedCache distributedCache,
    ItemPriceSnapshotStore snapshotStore,
    ILogger<PricingProvider> logger) : IPricingCatalog, IPricingHistoryProvider
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly ConcurrentDictionary<string, CatalogSnapshot> MemoryCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.OrdinalIgnoreCase);

    // skinport's price-history api is rate-limitedby only 8 requests/5 minutes, so caching results for 6 hours
    private static readonly TimeSpan HistoryCacheDuration = TimeSpan.FromHours(6);

    private static readonly ConcurrentDictionary<string, HistorySnapshot> HistoryMemoryCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> HistoryLocks = new(StringComparer.OrdinalIgnoreCase);

    public async Task<PriceQuote?> GetPriceAsync(
        string marketHashName,
        string currency,
        CancellationToken cancellationToken)
    {
        var item = await GetItemAsync(marketHashName, currency, cancellationToken);
        if (item is null)
        {
            return null;
        }

        var amount = item.SuggestedPrice
            ?? item.MedianPrice
            ?? item.MeanPrice
            ?? item.MinPrice
            ?? item.MaxPrice;

        return amount is null
            ? null
            : new PriceQuote(
                item.MarketHashName,
                amount.Value,
                item.Currency,
                item.Source,
                item.RetrievedAt);
    }

    public async Task<IReadOnlyList<PricingCatalogItemDto>> GetItemsAsync(
        IReadOnlyCollection<string> marketHashNames,
        string currency,
        CancellationToken cancellationToken)
    {
        if (marketHashNames.Count == 0)
        {
            return [];
        }

        var catalog = await GetCatalogAsync(currency, cancellationToken);
        if (catalog is null)
        {
            return [];
        }

        return marketHashNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => catalog.TryGetValue(name, out var item)
                ? item
                : null)
            .Where(item => item is not null)
            .Select(item => item!)
            .ToList();
    }

    public async Task<PricingCatalogItemDto?> GetItemAsync(
        string marketHashName,
        string currency,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(marketHashName))
        {
            return null;
        }

        var catalog = await GetCatalogAsync(currency, cancellationToken);
        if (catalog is null)
        {
            return null;
        }

        if (!catalog.TryGetValue(marketHashName, out var item))
        {
            return null;
        }

        await snapshotStore.RecordDailySnapshotAsync(
            item.MarketHashName, item.Currency,
            item.MinPrice, item.MedianPrice, item.MeanPrice, item.MaxPrice, item.Quantity,
            cancellationToken);
        return item;
    }

    public async Task<PricingHistoryDto?> GetHistoryAsync(
        string marketHashName,
        string currency,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(marketHashName))
        {
            return null;
        }

        var normalizedCurrency = NormalizeCurrency(currency);

        var periods = await GetSkinportPeriodsAsync(marketHashName, normalizedCurrency, cancellationToken);

        var today = periods?.FirstOrDefault(period => period.Period == "24h");
        if (today is not null)
        {
            await snapshotStore.RecordDailySnapshotAsync(
                marketHashName, normalizedCurrency,
                today.MinPrice, today.MedianPrice, today.AvgPrice, today.MaxPrice, today.Volume,
                cancellationToken);
        }

        await snapshotStore.EnsureBackfilledFromSteamAsync(marketHashName, normalizedCurrency, cancellationToken);

        var dailyPoints = await snapshotStore.GetDailySnapshotsAsync(marketHashName, normalizedCurrency, cancellationToken);

        if (periods is null && dailyPoints.Count == 0)
        {
            return null;
        }

        return new PricingHistoryDto(marketHashName, normalizedCurrency, periods ?? [], dailyPoints);
    }

    // IPricingHistoryProvider forwards straight to the snapshot store 
    // see ItemPriceSnapshotStore for why backfilling is owned there, not here
    public Task<int> BackfillFromSteamAsync(string marketHashName, CancellationToken cancellationToken) =>
        snapshotStore.BackfillFromSteamAsync(marketHashName, cancellationToken);

    private async Task<IReadOnlyList<PricingHistoryPeriodDto>?> GetSkinportPeriodsAsync(
        string marketHashName,
        string currency,
        CancellationToken cancellationToken)
    {
        var cacheKey = GetHistoryCacheKey(marketHashName, currency);

        if (HistoryMemoryCache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached.Periods;
        }

        var gate = HistoryLocks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (HistoryMemoryCache.TryGetValue(cacheKey, out cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
            {
                return cached.Periods;
            }

            var distributedSnapshot = await ReadHistoryDistributedCacheAsync(cacheKey, cancellationToken);
            if (distributedSnapshot is not null && distributedSnapshot.ExpiresAt > DateTimeOffset.UtcNow)
            {
                HistoryMemoryCache[cacheKey] = distributedSnapshot;
                return distributedSnapshot.Periods;
            }

            var fetched = await FetchHistoryAsync(marketHashName, currency, cacheKey, cancellationToken);
            if (fetched is not null)
            {
                return fetched.Periods;
            }

            return (cached ?? distributedSnapshot)?.Periods;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<HistorySnapshot?> FetchHistoryAsync(
        string marketHashName,
        string currency,
        string cacheKey,
        CancellationToken cancellationToken)
    {
        var requestUri =
            $"https://api.skinport.com/v1/sales/history?app_id=730&currency={Uri.EscapeDataString(currency)}&market_hash_name={Uri.EscapeDataString(marketHashName)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Skinport sales history request failed with status code {StatusCode}.",
                (int)response.StatusCode);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var items = await JsonSerializer.DeserializeAsync<List<SkinportHistoryItemDto>>(stream, JsonOptions, cancellationToken);
        var match = items?.FirstOrDefault(item =>
            string.Equals(item.MarketHashName, marketHashName, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            return null;
        }

        var snapshot = new HistorySnapshot(BuildHistoryPeriods(match), DateTimeOffset.UtcNow.Add(HistoryCacheDuration));
        HistoryMemoryCache[cacheKey] = snapshot;

        try
        {
            var cachedJson = JsonSerializer.Serialize(snapshot, JsonOptions);
            await distributedCache.SetStringAsync(
                cacheKey,
                cachedJson,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = HistoryCacheDuration },
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Writing Skinport sales history to distributed cache failed.");
        }

        return snapshot;
    }

    private async Task<HistorySnapshot?> ReadHistoryDistributedCacheAsync(string cacheKey, CancellationToken cancellationToken)
    {
        try
        {
            var cachedJson = await distributedCache.GetStringAsync(cacheKey, cancellationToken);
            if (string.IsNullOrWhiteSpace(cachedJson))
            {
                return null;
            }

            return JsonSerializer.Deserialize<HistorySnapshot>(cachedJson, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Reading Skinport sales history from distributed cache failed.");
            return null;
        }
    }

    private static IReadOnlyList<PricingHistoryPeriodDto> BuildHistoryPeriods(SkinportHistoryItemDto item)
    {
        var periods = new List<PricingHistoryPeriodDto>();

        AddHistoryPeriod(periods, "24h", item.Last24Hours);
        AddHistoryPeriod(periods, "7d", item.Last7Days);
        AddHistoryPeriod(periods, "30d", item.Last30Days);
        AddHistoryPeriod(periods, "90d", item.Last90Days);

        return periods;
    }

    private static void AddHistoryPeriod(List<PricingHistoryPeriodDto> periods, string period, SkinportPeriodStatsDto? stats)
    {
        if (stats is null)
        {
            return;
        }

        periods.Add(new PricingHistoryPeriodDto(period, stats.Min, stats.Max, stats.Avg, stats.Median, stats.Volume));
    }

    private static string GetHistoryCacheKey(string marketHashName, string currency)
    {
        return $"pricing:skinport:history:730:{currency}:{marketHashName.ToLowerInvariant()}:v1";
    }

    private async Task<IReadOnlyDictionary<string, PricingCatalogItemDto>?> GetCatalogAsync(
        string currency,
        CancellationToken cancellationToken)
    {
        var normalizedCurrency = NormalizeCurrency(currency);
        var cacheKey = GetCacheKey(normalizedCurrency);

        if (MemoryCache.TryGetValue(cacheKey, out var cachedSnapshot) &&
            cachedSnapshot.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cachedSnapshot.Items;
        }

        var gate = Locks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (MemoryCache.TryGetValue(cacheKey, out cachedSnapshot) &&
                cachedSnapshot.ExpiresAt > DateTimeOffset.UtcNow)
            {
                return cachedSnapshot.Items;
            }

            var distributedSnapshot = await ReadDistributedCacheAsync(cacheKey, cancellationToken);
            if (distributedSnapshot is not null &&
                distributedSnapshot.ExpiresAt > DateTimeOffset.UtcNow)
            {
                MemoryCache[cacheKey] = distributedSnapshot;
                return distributedSnapshot.Items;
            }

            var fetchedSnapshot = await FetchCatalogAsync(normalizedCurrency, cacheKey, cancellationToken);
            if (fetchedSnapshot is not null)
            {
                return fetchedSnapshot.Items;
            }

            if (cachedSnapshot is not null)
            {
                return cachedSnapshot.Items;
            }

            return distributedSnapshot?.Items;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<CatalogSnapshot?> FetchCatalogAsync(
        string currency,
        string cacheKey,
        CancellationToken cancellationToken)
    {
        var requestUri =
            $"https://api.skinport.com/v1/items?app_id=730&currency={Uri.EscapeDataString(currency)}&tradable=0";

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Skinport pricing request failed with status code {StatusCode}.",
                (int)response.StatusCode);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var items = await JsonSerializer.DeserializeAsync<List<PricingItemDto>>(stream, JsonOptions, cancellationToken);
        if (items is null)
        {
            return null;
        }

        var fetchedAt = DateTimeOffset.UtcNow;
        var snapshot = new CatalogSnapshot(
            items
                .Where(item => !string.IsNullOrWhiteSpace(item.MarketHashName))
                .Select(item => item.ToPricingItemDto(currency, fetchedAt))
                .GroupBy(item => item.MarketHashName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderByDescending(item => item.Quantity).First(),
                    StringComparer.OrdinalIgnoreCase),
            fetchedAt.Add(CacheDuration));

        MemoryCache[cacheKey] = snapshot;

        try
        {
            var cachedJson = JsonSerializer.Serialize(ToCacheDto(snapshot), JsonOptions);
            await distributedCache.SetStringAsync(
                cacheKey,
                cachedJson,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = CacheDuration
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Writing Skinport pricing catalog to distributed cache failed.");
        }

        return snapshot;
    }

    private async Task<CatalogSnapshot?> ReadDistributedCacheAsync(
        string cacheKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var cachedJson = await distributedCache.GetStringAsync(cacheKey, cancellationToken);
            if (string.IsNullOrWhiteSpace(cachedJson))
            {
                return null;
            }

            var cached = JsonSerializer.Deserialize<CatalogCacheDto>(cachedJson, JsonOptions);
            if (cached is null)
            {
                return null;
            }

            var items = cached.Items
                .Where(item => !string.IsNullOrWhiteSpace(item.MarketHashName))
                .Select(item => item.ToPricingItemDto(item.Currency, cached.FetchedAt))
                .GroupBy(item => item.MarketHashName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderByDescending(item => item.Quantity).First(),
                    StringComparer.OrdinalIgnoreCase);

            return new CatalogSnapshot(items, cached.FetchedAt.Add(CacheDuration));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Reading Skinport pricing catalog from distributed cache failed.");
            return null;
        }
    }

    private static string GetCacheKey(string currency)
    {
        return $"pricing:skinport:730:{currency}:v1";
    }

    private static string NormalizeCurrency(string currency)
    {
        return string.IsNullOrWhiteSpace(currency) ? "EUR" : currency.Trim().ToUpperInvariant();
    }

    private sealed record CatalogSnapshot(
        IReadOnlyDictionary<string, PricingCatalogItemDto> Items,
        DateTimeOffset ExpiresAt);

    private sealed record HistorySnapshot(IReadOnlyList<PricingHistoryPeriodDto> Periods, DateTimeOffset ExpiresAt);

    private sealed record SkinportPeriodStatsDto
    {
        [JsonPropertyName("min")]
        public decimal? Min { get; init; }

        [JsonPropertyName("max")]
        public decimal? Max { get; init; }

        [JsonPropertyName("avg")]
        public decimal? Avg { get; init; }

        [JsonPropertyName("median")]
        public decimal? Median { get; init; }

        [JsonPropertyName("volume")]
        public int Volume { get; init; }
    }

    private sealed record SkinportHistoryItemDto
    {
        [JsonPropertyName("market_hash_name")]
        public string MarketHashName { get; init; } = string.Empty;

        [JsonPropertyName("currency")]
        public string Currency { get; init; } = string.Empty;

        [JsonPropertyName("last_24_hours")]
        public SkinportPeriodStatsDto? Last24Hours { get; init; }

        [JsonPropertyName("last_7_days")]
        public SkinportPeriodStatsDto? Last7Days { get; init; }

        [JsonPropertyName("last_30_days")]
        public SkinportPeriodStatsDto? Last30Days { get; init; }

        [JsonPropertyName("last_90_days")]
        public SkinportPeriodStatsDto? Last90Days { get; init; }
    }

    private sealed record CatalogCacheDto(
        DateTimeOffset FetchedAt,
        List<PricingItemDto> Items);

    private static CatalogCacheDto ToCacheDto(CatalogSnapshot snapshot)
    {
        return new CatalogCacheDto(
            snapshot.ExpiresAt - CacheDuration,
            snapshot.Items.Values
                .Select(item => new PricingItemDto
                {
                    MarketHashName = item.MarketHashName,
                    Currency = item.Currency,
                    SuggestedPrice = item.SuggestedPrice,
                    ItemPage = item.ItemPage,
                    MarketPage = item.MarketPage,
                    MinPrice = item.MinPrice,
                    MaxPrice = item.MaxPrice,
                    MeanPrice = item.MeanPrice,
                    MedianPrice = item.MedianPrice,
                    Quantity = item.Quantity,
                    UpdatedAt = item.UpdatedAt is null ? null : item.UpdatedAt.Value.ToUnixTimeSeconds()
                })
                .ToList());
    }

    private sealed record PricingItemDto
    {
        [JsonPropertyName("market_hash_name")]
        public string MarketHashName { get; init; } = string.Empty;

        [JsonPropertyName("currency")]
        public string Currency { get; init; } = string.Empty;

        [JsonPropertyName("suggested_price")]
        public decimal? SuggestedPrice { get; init; }

        [JsonPropertyName("item_page")]
        public string? ItemPage { get; init; }

        [JsonPropertyName("market_page")]
        public string? MarketPage { get; init; }

        [JsonPropertyName("min_price")]
        public decimal? MinPrice { get; init; }

        [JsonPropertyName("max_price")]
        public decimal? MaxPrice { get; init; }

        [JsonPropertyName("mean_price")]
        public decimal? MeanPrice { get; init; }

        [JsonPropertyName("median_price")]
        public decimal? MedianPrice { get; init; }

        [JsonPropertyName("quantity")]
        public int Quantity { get; init; }

        [JsonPropertyName("updated_at")]
        public long? UpdatedAt { get; init; }

        public PricingCatalogItemDto ToPricingItemDto(string currency, DateTimeOffset fetchedAt)
        {
            return new PricingCatalogItemDto(
                MarketHashName,
                string.IsNullOrWhiteSpace(Currency) ? currency : Currency,
                SuggestedPrice,
                MeanPrice,
                MedianPrice,
                MinPrice,
                MaxPrice,
                Quantity,
                ItemPage,
                MarketPage,
                "skinport",
                fetchedAt,
                UpdatedAt is null ? null : DateTimeOffset.FromUnixTimeSeconds(UpdatedAt.Value));
        }
    }
}
