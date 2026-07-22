using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using LootBase.Application.Abstractions.Pricing;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace LootBase.Infrastructure.Pricing;

// Price and history both come from one bulk Skinport call (/v1/sales/history); current price = last_24_hours
public sealed class PricingProvider(
    HttpClient httpClient,
    IDistributedCache distributedCache,
    ItemPriceSnapshotStore snapshotStore,
    ILogger<PricingProvider> logger) : IPricingCatalog, IPricingHistoryProvider
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly ConcurrentDictionary<string, CatalogSnapshot> MemoryCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Lock BulkBackfillStatusLock = new();
    private static BulkBackfillStatusDto bulkBackfillStatus = BulkBackfillStatusDto.Idle;

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

        var amount = item.MedianPrice ?? item.MeanPrice ?? item.MinPrice ?? item.MaxPrice;

        return amount is null
            ? null
            : new PriceQuote(
                item.MarketHashName,
                amount.Value,
                item.Currency,
                item.Source,
                item.RetrievedAt
            );
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
            .Select(name => catalog.Items.TryGetValue(name, out var entry) ? entry : null)
            .Where(entry => entry is not null)
            .Select(entry => ToCatalogItemDto(entry!, currency, entry!.Last24Hours, catalog.FetchedAt))
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
        if (catalog is null || !catalog.Items.TryGetValue(marketHashName, out var entry))
        {
            return null;
        }

        var item = ToCatalogItemDto(entry, currency, entry.Last24Hours, catalog.FetchedAt);

        await snapshotStore.RecordDailySnapshotAsync(
            item.MarketHashName, item.Currency,
            entry.Last24Hours?.Min, entry.Last24Hours?.Median, entry.Last24Hours?.Avg, entry.Last24Hours?.Max, entry.Last24Hours?.Volume ?? 0,
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

        var catalog = await GetCatalogAsync(currency, cancellationToken);
        var entry = catalog is not null && catalog.Items.TryGetValue(marketHashName, out var found) ? found : null;
        var periods = entry is null ? null : BuildHistoryPeriods(entry);

        var today = periods?.FirstOrDefault(period => period.Period == "24h");
        if (today is not null)
        {
            await snapshotStore.RecordDailySnapshotAsync(
                marketHashName, currency,
                today.MinPrice, today.MedianPrice, today.AvgPrice, today.MaxPrice, today.Volume,
                cancellationToken);
        }

        await snapshotStore.EnsureBackfilledFromSteamAsync(marketHashName, currency, cancellationToken);

        if (periods is not null)
        {
            await snapshotStore.SeedFromSkinportPeriodsAsync(marketHashName, currency, periods, cancellationToken);
        }

        var dailyPoints = await snapshotStore.GetDailySnapshotsAsync(marketHashName, currency, cancellationToken);

        if (periods is null && dailyPoints.Count == 0)
        {
            return null;
        }

        return new PricingHistoryDto(marketHashName, currency, periods ?? [], dailyPoints);
    }

    public Task<int?> BackfillFromSteamAsync(string marketHashName, CancellationToken cancellationToken) =>
        snapshotStore.BackfillFromSteamAsync(marketHashName, cancellationToken);

    public async Task BackfillAllFromSteamAsync(string currency, CancellationToken cancellationToken)
    {
        var catalog = await GetCatalogAsync(currency, cancellationToken);
        if (catalog is null)
        {
            logger.LogWarning("Bulk Steam backfill aborted: Skinport catalog unavailable.");
            SetBulkBackfillStatus(status => status with
            {
                IsRunning = false,
                Currency = currency,
                FinishedAt = DateTimeOffset.UtcNow,
                LastError = "Skinport catalog unavailable."
            });
            return;
        }

        var names = catalog.Items.Keys.ToList();
        logger.LogInformation("Bulk Steam backfill starting for {Count} items ({Currency}).", names.Count, currency);

        SetBulkBackfillStatus(status => status with { Currency = currency, TotalItems = names.Count });

        var processed = 0;
        var imported = 0;
        var alreadyCovered = 0;
        try
        {
            foreach (var name in names)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = await snapshotStore.BulkBackfillItemAsync(name, currency, cancellationToken);
                imported += result.Imported;
                if (result.AlreadyCovered)
                {
                    alreadyCovered++;
                }
                processed++;

                SetBulkBackfillStatus(status => status with
                {
                    Processed = processed,
                    Imported = imported,
                    AlreadyCovered = alreadyCovered
                });

                if (processed % 50 == 0 || processed == names.Count)
                {
                    logger.LogInformation(
                        "Bulk Steam backfill progress: {Processed}/{Total} items checked ({AlreadyCovered} already covered), {Imported} rows imported so far.",
                        processed, names.Count, alreadyCovered, imported);
                }
            }

            logger.LogInformation(
                "Bulk Steam backfill finished: {Processed} items checked ({AlreadyCovered} already covered), {Imported} rows imported.",
                processed, alreadyCovered, imported);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Bulk Steam backfill crashed after {Processed}/{Total} items.", processed, names.Count);
            SetBulkBackfillStatus(status => status with { LastError = ex.Message });
        }
        finally
        {
            SetBulkBackfillStatus(status => status with { IsRunning = false, FinishedAt = DateTimeOffset.UtcNow });
        }
    }

    public bool TryStartBulkBackfill()
    {
        lock (BulkBackfillStatusLock)
        {
            if (bulkBackfillStatus.IsRunning)
            {
                return false;
            }

            bulkBackfillStatus = new BulkBackfillStatusDto(
                IsRunning: true,
                Currency: null,
                TotalItems: 0,
                Processed: 0,
                AlreadyCovered: 0,
                Imported: 0,
                StartedAt: DateTimeOffset.UtcNow,
                FinishedAt: null,
                LastError: null);
            return true;
        }
    }

    public BulkBackfillStatusDto GetBulkBackfillStatus()
    {
        lock (BulkBackfillStatusLock)
        {
            return bulkBackfillStatus;
        }
    }

    private static void SetBulkBackfillStatus(Func<BulkBackfillStatusDto, BulkBackfillStatusDto> update)
    {
        lock (BulkBackfillStatusLock)
        {
            bulkBackfillStatus = update(bulkBackfillStatus);
        }
    }

    private static PricingCatalogItemDto ToCatalogItemDto(
        SkinportHistoryItemDto entry,
        string currency,
        SkinportPeriodStatsDto? window,
        DateTimeOffset retrievedAt)
    {
        return new PricingCatalogItemDto(
            entry.MarketHashName,
            string.IsNullOrWhiteSpace(entry.Currency) ? currency : entry.Currency,
            window?.Avg,
            window?.Median,
            window?.Min,
            window?.Max,
            entry.ItemPage,
            entry.MarketPage,
            "skinport",
            retrievedAt);
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

    private async Task<CatalogSnapshot?> GetCatalogAsync(string currency, CancellationToken cancellationToken)
    {
        var cacheKey = GetCacheKey(currency);

        if (MemoryCache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached;
        }

        var gate = Locks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (MemoryCache.TryGetValue(cacheKey, out cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
            {
                return cached;
            }

            var distributedSnapshot = await ReadDistributedCacheAsync(cacheKey, cancellationToken);
            if (distributedSnapshot is not null && distributedSnapshot.ExpiresAt > DateTimeOffset.UtcNow)
            {
                MemoryCache[cacheKey] = distributedSnapshot;
                return distributedSnapshot;
            }

            var fetched = await FetchCatalogAsync(currency, cacheKey, cancellationToken);
            return fetched ?? cached ?? distributedSnapshot;
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
        var requestUri = $"https://api.skinport.com/v1/sales/history?app_id=730&currency={Uri.EscapeDataString(currency)}";

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
        if (items is null)
        {
            return null;
        }

        var fetchedAt = DateTimeOffset.UtcNow;
        var snapshot = new CatalogSnapshot(
            items
                .Where(item => !string.IsNullOrWhiteSpace(item.MarketHashName))
                .GroupBy(item => item.MarketHashName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderByDescending(item => item.Last90Days?.Volume ?? 0).First(),
                    StringComparer.OrdinalIgnoreCase),
            fetchedAt,
            fetchedAt.Add(CacheDuration));

        MemoryCache[cacheKey] = snapshot;

        try
        {
            var cachedJson = JsonSerializer.Serialize(snapshot, JsonOptions);
            await distributedCache.SetStringAsync(
                cacheKey,
                cachedJson,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheDuration },
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Writing Skinport pricing/history cache failed.");
        }

        return snapshot;
    }

    private async Task<CatalogSnapshot?> ReadDistributedCacheAsync(string cacheKey, CancellationToken cancellationToken)
    {
        try
        {
            var cachedJson = await distributedCache.GetStringAsync(cacheKey, cancellationToken);
            return string.IsNullOrWhiteSpace(cachedJson)
                ? null
                : JsonSerializer.Deserialize<CatalogSnapshot>(cachedJson, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Reading Skinport pricing/history cache failed.");
            return null;
        }
    }

    private static string GetCacheKey(string currency)
    {
        return $"pricing:skinport:history:730:{currency}:v1";
    }


    private sealed record CatalogSnapshot(
        IReadOnlyDictionary<string, SkinportHistoryItemDto> Items,
        DateTimeOffset FetchedAt,
        DateTimeOffset ExpiresAt);

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

        [JsonPropertyName("item_page")]
        public string? ItemPage { get; init; }

        [JsonPropertyName("market_page")]
        public string? MarketPage { get; init; }

        [JsonPropertyName("last_24_hours")]
        public SkinportPeriodStatsDto? Last24Hours { get; init; }

        [JsonPropertyName("last_7_days")]
        public SkinportPeriodStatsDto? Last7Days { get; init; }

        [JsonPropertyName("last_30_days")]
        public SkinportPeriodStatsDto? Last30Days { get; init; }

        [JsonPropertyName("last_90_days")]
        public SkinportPeriodStatsDto? Last90Days { get; init; }
    }
}
