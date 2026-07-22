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

    // Steam Market's rate limit isn't documented beyond the explicit 429
    // response, so we self-impose a global (not per-item) minimum gap
    // between calls on top of reacting to that signal directly.
    private static readonly Lock SteamThrottleLock = new();
    private static readonly TimeSpan MinIntervalBetweenSteamCalls = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan SteamCircuitCooldown = TimeSpan.FromMinutes(30);

    // Only used for ambiguous failures (exceptions, timeouts) - a genuine
    // 429 opens the circuit immediately, no streak needed. A run of plain
    // "no data" responses (400/404/success:false) is expected and common
    // during a bulk scan - stickers, agents, graffiti etc. Steam has no
    // listing for - and never counts against this.
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

    // For exceptions/timeouts only - an ambiguous signal, so it takes
    // several in a row before we treat it like a real block.
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

    // Best-effort, called on every history view. Skips items that already
    // have enough real coverage, respects the global throttle/circuit
    // breaker, and never throws - a broken cookie degrades silently back to
    // whatever SeedFromSkinportPeriodsAsync provides.
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

    // Same rules as EnsureBackfilledFromSteamAsync, but WAITS for the
    // throttle window to open instead of skipping the item - there's no
    // impatient page load on the other end of a one-time bulk run. Shares
    // the same static throttle/circuit state, so a bulk run and a live user
    // request can never combine to exceed the rate budget.
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

    private async Task<bool> HasSufficientCoverageAsync(
        string marketHashName,
        string currency,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var oldestCapturedDate = await dbContext.ItemPriceSnapshots
            .Where(snapshot => snapshot.MarketHashName == marketHashName && snapshot.Currency == currency)
            .OrderBy(snapshot => snapshot.CapturedDate)
            .Select(snapshot => (DateOnly?)snapshot.CapturedDate)
            .FirstOrDefaultAsync(cancellationToken);

        return oldestCapturedDate is not null && oldestCapturedDate.Value <= today.AddDays(-SufficientCoverageDays);
    }

    private static async Task WaitForSteamThrottleAsync(CancellationToken cancellationToken)
    {
        while (!TryEnterSteamCallWindow())
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }

    // Returns null only when Steam explicitly rate limited us (429) -
    // distinct from 0, which covers both "no data for this item" and
    // "call succeeded but had nothing new to add". Callers use null to
    // decide whether to open the circuit breaker.
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
