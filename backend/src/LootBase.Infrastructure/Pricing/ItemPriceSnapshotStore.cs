using System.Collections.Concurrent;
using LootBase.Application.Abstractions.Pricing;
using LootBase.Domain.Pricing;
using LootBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LootBase.Infrastructure.Pricing;

public sealed class ItemPriceSnapshotStore(
    LootBaseDbContext dbContext,
    ISteamMarketHistoryClient steamMarketHistory,
    ILogger<ItemPriceSnapshotStore> logger)
{
    // how far back auto-backfill reaches
    private const int BackfillTargetDays = 90;
    private const int SufficientCoverageDays = 85;

    private static readonly ConcurrentDictionary<string, DateOnly> LastSnapshotDateByItem = new(StringComparer.OrdinalIgnoreCase);

    // Guards EnsureBackfilledFromSteamAsync: once attempted for an item
    // (success or failure), don't retry for a day, so opening the same
    // item's history repeatedly can't hammer Steam's undocumented endpoint
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

        // checks, that only the last 90 days are imported + ensures, that nothing is imported twice
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
