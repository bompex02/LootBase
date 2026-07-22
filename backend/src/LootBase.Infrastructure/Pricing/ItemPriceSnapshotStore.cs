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
    private const int BackfillTargetDays = 90;
    private const int SufficientCoverageDays = 85;

    private static readonly ConcurrentDictionary<string, DateOnly> LastSnapshotDateByItem = new(StringComparer.OrdinalIgnoreCase);

    // Steam never documented its rate limit, so we self-impose a global minimum gap between calls too
    private static readonly Lock SteamThrottleLock = new();
    private static readonly TimeSpan MinIntervalBetweenSteamCalls = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan SteamCircuitCooldown = TimeSpan.FromMinutes(30);

    // Only for ambiguous failures - a real 429 opens the circuit right away, no streak needed
    private const int MaxConsecutiveFailuresBeforeCircuit = 5;
    private static DateTimeOffset lastSteamCallAt = DateTimeOffset.MinValue;
    private static DateTimeOffset steamCircuitOpenUntil = DateTimeOffset.MinValue;
    private static int consecutiveSteamFailures;

    private static bool TryEnterSteamCallWindow()
    {
        lock (SteamThrottleLock)
        {
            var now = DateTimeOffset.UtcNow;
            if (now < steamCircuitOpenUntil || now - lastSteamCallAt < MinIntervalBetweenSteamCalls)
            {
                return false;
            }

            lastSteamCallAt = now;
            return true;
        }
    }

    private static void ResetSteamFailureStreak()
    {
        lock (SteamThrottleLock)
        {
            consecutiveSteamFailures = 0;
        }
    }

    private void RegisterSteamRateLimited(string marketHashName)
    {
        lock (SteamThrottleLock)
        {
            steamCircuitOpenUntil = DateTimeOffset.UtcNow.Add(SteamCircuitCooldown);
            consecutiveSteamFailures = 0;
            logger.LogWarning(
                "Steam circuit opened for {Cooldown} after an explicit 429 on {MarketHashName}.",
                SteamCircuitCooldown, marketHashName);
        }
    }

    // Ambiguous signal, so it takes a few in a row before we treat it like a real block
    private void RegisterSteamCallError(string marketHashName)
    {
        lock (SteamThrottleLock)
        {
            consecutiveSteamFailures++;
            if (consecutiveSteamFailures < MaxConsecutiveFailuresBeforeCircuit)
            {
                return;
            }

            steamCircuitOpenUntil = DateTimeOffset.UtcNow.Add(SteamCircuitCooldown);
            consecutiveSteamFailures = 0;
            logger.LogWarning(
                "Steam circuit opened for {Cooldown} after {Count} consecutive call errors (last: {MarketHashName}).",
                SteamCircuitCooldown, MaxConsecutiveFailuresBeforeCircuit, marketHashName);
        }
    }

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

    // Seeds historical price snapshots from Skinport period aggregates (7d, 30d, and 90d) if they do not already exist    
    public async Task SeedFromSkinportPeriodsAsync(
        string marketHashName,
        string currency,
        IReadOnlyList<PricingHistoryPeriodDto> periods,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var anchors = new (string Period, int DaysAgo)[] { ("7d", 7), ("30d", 30), ("90d", 90) };

        foreach (var (periodKey, daysAgo) in anchors)
        {
            var period = periods.FirstOrDefault(candidate => candidate.Period == periodKey);
            if (period is null)
            {
                continue;
            }

            var date = today.AddDays(-daysAgo);
            var exists = await dbContext.ItemPriceSnapshots.AnyAsync(
                snapshot => snapshot.MarketHashName == marketHashName &&
                    snapshot.Currency == currency &&
                    snapshot.CapturedDate == date,
                cancellationToken);

            if (exists)
            {
                continue;
            }

            dbContext.ItemPriceSnapshots.Add(new ItemPriceSnapshot
            {
                MarketHashName = marketHashName,
                Currency = currency,
                CapturedDate = date,
                MinPrice = period.MinPrice,
                MedianPrice = period.MedianPrice,
                MeanPrice = period.AvgPrice,
                MaxPrice = period.MaxPrice,
                Quantity = period.Volume,
                Source = "skinport_period"
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // Best-effort, called on every history view - never throws, just falls
    // back to whatever SeedFromSkinportPeriodsAsync already provided
    public async Task EnsureBackfilledFromSteamAsync(
        string marketHashName,
        string currency,
        CancellationToken cancellationToken)
    {
        if (await HasSufficientCoverageAsync(marketHashName, currency, cancellationToken) ||
            !TryEnterSteamCallWindow())
        {
            return;
        }

        try
        {
            await BackfillFromSteamAsync(marketHashName, cancellationToken);
        }
        catch (Exception ex)
        {
            RegisterSteamCallError(marketHashName);
            logger.LogWarning(ex, "Auto-backfilling {MarketHashName} from Steam failed.", marketHashName);
        }
    }

    // Same as EnsureBackfilledFromSteamAsync, but waits for the throttle
    // window instead of skipping - fine here, nobody's waiting on a page load
    public async Task<BulkBackfillItemResult> BulkBackfillItemAsync(
        string marketHashName,
        string currency,
        CancellationToken cancellationToken)
    {
        if (await HasSufficientCoverageAsync(marketHashName, currency, cancellationToken))
        {
            return new BulkBackfillItemResult(Imported: 0, AlreadyCovered: true);
        }

        await WaitForSteamThrottleAsync(cancellationToken);

        try
        {
            var imported = await BackfillFromSteamAsync(marketHashName, cancellationToken);
            return new BulkBackfillItemResult(Imported: imported ?? 0, AlreadyCovered: false);
        }
        catch (Exception ex)
        {
            RegisterSteamCallError(marketHashName);
            logger.LogWarning(ex, "Bulk backfill for {MarketHashName} failed.", marketHashName);
            return new BulkBackfillItemResult(Imported: 0, AlreadyCovered: false);
        }
    }

    // Only a "steam" row counts as real coverage - a single Skinport seed
    // anchor would otherwise look "covered" forever and block the real backfill
    private async Task<bool> HasSufficientCoverageAsync(
        string marketHashName,
        string currency,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var oldestSteamCapturedDate = await dbContext.ItemPriceSnapshots
            .Where(snapshot => snapshot.MarketHashName == marketHashName &&
                snapshot.Currency == currency &&
                snapshot.Source == "steam")
            .OrderBy(snapshot => snapshot.CapturedDate)
            .Select(snapshot => (DateOnly?)snapshot.CapturedDate)
            .FirstOrDefaultAsync(cancellationToken);

        return oldestSteamCapturedDate is not null && oldestSteamCapturedDate.Value <= today.AddDays(-SufficientCoverageDays);
    }

    private static async Task WaitForSteamThrottleAsync(CancellationToken cancellationToken)
    {
        while (!TryEnterSteamCallWindow())
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }

    // Null means rate-limited (429); 0 means it just had nothing to add
    public async Task<int?> BackfillFromSteamAsync(string marketHashName, CancellationToken cancellationToken)
    {
        var result = await steamMarketHistory.GetPriceHistoryAsync(marketHashName, cancellationToken);

        if (result.Outcome == SteamMarketHistoryOutcome.RateLimited)
        {
            RegisterSteamRateLimited(marketHashName);
            return null;
        }

        ResetSteamFailureStreak();

        if (result.Outcome == SteamMarketHistoryOutcome.NoData || result.Data is null)
        {
            return 0;
        }

        var data = result.Data;
        if (data.Points.Count == 0)
        {
            return 0;
        }

        // checks, that only the last 90 days are imported + ensures, that nothing is imported twice
        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-BackfillTargetDays);
        var relevantPoints = data.Points.Where(point => point.Date >= cutoff).ToList();
        if (relevantPoints.Count == 0)
        {
            return 0;
        }

        var existingDates = await dbContext.ItemPriceSnapshots
            .Where(snapshot => snapshot.MarketHashName == marketHashName && snapshot.Currency == data.Currency)
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
                Currency = data.Currency,
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

public sealed record BulkBackfillItemResult(int Imported, bool AlreadyCovered);
