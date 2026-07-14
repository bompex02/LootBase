using System.Collections.Concurrent;
using LootBase.Application.Abstractions.Pricing;
using LootBase.Domain.Pricing;
using LootBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LootBase.Infrastructure.Pricing;

// Owns our own daily price history (the ItemPriceSnapshots table) and its
// only external top-up source, Steam Market's price history. Kept separate
// from PricingProvider, which only talks to Skinport - this class is about
// what we persist, not about any one upstream API.
public sealed class ItemPriceSnapshotStore(
    LootBaseDbContext dbContext,
    ISteamMarketHistoryClient steamMarketHistory,
    ILogger<ItemPriceSnapshotStore> logger)
{
    // How far back auto-backfill reaches, and how close to that existing
    // coverage must already be before we skip it.
    private const int BackfillTargetDays = 90;
    private const int SufficientCoverageDays = 85;

    // One entry per "currency:marketHashName" recording the UTC date we last
    // wrote a daily snapshot row for it, so hot paths (item views, inventory
    // pricing) that record repeatedly only touch the database once per item
    // per day instead of on every call.
    private static readonly ConcurrentDictionary<string, DateOnly> LastSnapshotDateByItem = new(StringComparer.OrdinalIgnoreCase);

    // Guards EnsureBackfilledFromSteamAsync: once attempted for an item
    // (success or failure), don't retry for a day, so opening the same
    // item's history repeatedly can't hammer Steam's undocumented endpoint.
    private static readonly ConcurrentDictionary<string, DateOnly> LastAutoBackfillAttemptByItem = new(StringComparer.OrdinalIgnoreCase);

    public async Task RecordDailySnapshotAsync(
        string marketHashName,
        string currency,
        decimal? minPrice,
        decimal? medianPrice,
        decimal? meanPrice,
        decimal? maxPrice,
        int quantity,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var key = $"{currency}:{marketHashName}";

        if (LastSnapshotDateByItem.TryGetValue(key, out var lastDate) && lastDate == today)
        {
            return;
        }

        try
        {
            var existing = await dbContext.ItemPriceSnapshots.FirstOrDefaultAsync(
                snapshot => snapshot.MarketHashName == marketHashName &&
                    snapshot.Currency == currency &&
                    snapshot.CapturedDate == today,
                cancellationToken);

            if (existing is null)
            {
                dbContext.ItemPriceSnapshots.Add(new ItemPriceSnapshot
                {
                    MarketHashName = marketHashName,
                    Currency = currency,
                    CapturedDate = today,
                    MinPrice = minPrice,
                    MedianPrice = medianPrice,
                    MeanPrice = meanPrice,
                    MaxPrice = maxPrice,
                    Quantity = quantity
                });
            }
            else
            {
                existing.MinPrice = minPrice;
                existing.MedianPrice = medianPrice;
                existing.MeanPrice = meanPrice;
                existing.MaxPrice = maxPrice;
                existing.Quantity = quantity;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            LastSnapshotDateByItem[key] = today;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Recording daily price snapshot for {MarketHashName} failed.", marketHashName);
        }
    }

    public async Task<IReadOnlyList<PricingHistoryDailyPointDto>> GetDailySnapshotsAsync(
        string marketHashName,
        string currency,
        CancellationToken cancellationToken)
    {
        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-BackfillTargetDays);

        var snapshots = await dbContext.ItemPriceSnapshots
            .Where(snapshot =>
                snapshot.MarketHashName == marketHashName &&
                snapshot.Currency == currency &&
                snapshot.CapturedDate >= cutoff)
            .OrderBy(snapshot => snapshot.CapturedDate)
            .ToListAsync(cancellationToken);

        return snapshots
            .Select(snapshot => new PricingHistoryDailyPointDto(
                snapshot.CapturedDate,
                snapshot.MinPrice,
                snapshot.MaxPrice,
                snapshot.MeanPrice,
                snapshot.MedianPrice,
                snapshot.Quantity))
            .ToList();
    }

    // Called on every history view so that the very first time anyone opens
    // an item's history, we transparently backfill up to 90 real days from
    // Steam Market (if Steam:MarketSessionCookie is configured) instead of
    // making users wait weeks for organic accumulation to fill the chart in.
    public async Task EnsureBackfilledFromSteamAsync(
        string marketHashName,
        string currency,
        CancellationToken cancellationToken)
    {
        var key = $"{currency}:{marketHashName}";
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        if (LastAutoBackfillAttemptByItem.TryGetValue(key, out var lastAttempt) && lastAttempt == today)
        {
            return;
        }

        LastAutoBackfillAttemptByItem[key] = today;

        var oldestCapturedDate = await dbContext.ItemPriceSnapshots
            .Where(snapshot => snapshot.MarketHashName == marketHashName && snapshot.Currency == currency)
            .OrderBy(snapshot => snapshot.CapturedDate)
            .Select(snapshot => (DateOnly?)snapshot.CapturedDate)
            .FirstOrDefaultAsync(cancellationToken);

        var alreadyCovered = oldestCapturedDate is not null &&
            oldestCapturedDate.Value <= today.AddDays(-SufficientCoverageDays);
        if (alreadyCovered)
        {
            return;
        }

        try
        {
            await BackfillFromSteamAsync(marketHashName, cancellationToken);
        }
        catch (Exception ex)
        {
            // Never let a Steam hiccup (parsing, timeout, rate limit) break a
            // regular history page view - the periods/organic data still work.
            logger.LogWarning(ex, "Auto-backfilling {MarketHashName} from Steam failed.", marketHashName);
        }
    }

    public async Task<int> BackfillFromSteamAsync(string marketHashName, CancellationToken cancellationToken)
    {
        var result = await steamMarketHistory.GetPriceHistoryAsync(marketHashName, cancellationToken);
        if (result is null || result.Points.Count == 0)
        {
            return 0;
        }

        // Only import what GetDailySnapshotsAsync actually reads back out
        // (last 90 days) - Steam's history goes back years, but nothing
        // older than that is ever read, so importing it would just be dead rows.
        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-BackfillTargetDays);
        var relevantPoints = result.Points.Where(point => point.Date >= cutoff).ToList();
        if (relevantPoints.Count == 0)
        {
            return 0;
        }

        var existingDates = await dbContext.ItemPriceSnapshots
            .Where(snapshot => snapshot.MarketHashName == marketHashName && snapshot.Currency == result.Currency)
            .Select(snapshot => snapshot.CapturedDate)
            .ToListAsync(cancellationToken);
        var existingDateSet = existingDates.ToHashSet();

        var imported = 0;
        foreach (var point in relevantPoints)
        {
            if (existingDateSet.Contains(point.Date))
            {
                continue;
            }

            dbContext.ItemPriceSnapshots.Add(new ItemPriceSnapshot
            {
                MarketHashName = marketHashName,
                Currency = result.Currency,
                CapturedDate = point.Date,
                MedianPrice = point.MedianPrice,
                Quantity = point.Volume,
                Source = "steam"
            });
            imported++;
        }

        if (imported > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return imported;
    }
}
