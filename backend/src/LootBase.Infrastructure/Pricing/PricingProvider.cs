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
    ILogger<PricingProvider> logger) : IPricingCatalog
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15); // 15min caching for pricing data
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly ConcurrentDictionary<string, CatalogSnapshot> MemoryCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.OrdinalIgnoreCase);

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

        return catalog.TryGetValue(marketHashName, out var item) ? item : null;
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
