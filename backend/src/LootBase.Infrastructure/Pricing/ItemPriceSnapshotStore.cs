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
    private const string NoSteamDataSource = "steam_no_data";

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

    // Without this, an item whose Steam call always throws (bad name encoding,
    // permanently malformed response, ...) never gets a no-data marker and sits
    // at the front of GetNextUncoveredItemsAsync's result forever - reprocessed
    // every batch and crowding out items that could actually succeed. A 429
    // isn't the item's fault (WaitForSteamThrottleAsync already backs off the
    // whole process for that), so it doesn't count towards this.
    private const int MaxConsecutiveBulkFailuresBeforeGivingUp = 5;
    private static readonly ConcurrentDictionary<string, int> ConsecutiveBulkFailuresByItem = new(StringComparer.OrdinalIgnoreCase);

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
        decimal? price,
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
                    Price = price,
                    Quantity = quantity
                });
            }
            else
            {
                existing.Price = price;
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

    // Same job as RecordDailySnapshotAsync, but for the whole catalog at once:
    // one query to see what's already captured today, one bulk insert for
    // the rest - instead of one SELECT + SaveChanges per item.
    public async Task<int> RecordDailySnapshotsBatchAsync(
        string currency,
        IReadOnlyList<DailySnapshotItemDto> items,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var alreadyCaptured = await dbContext.ItemPriceSnapshots
            .Where(snapshot => snapshot.Currency == currency && snapshot.CapturedDate == today)
            .Select(snapshot => snapshot.MarketHashName)
            .ToHashSetAsync(StringComparer.OrdinalIgnoreCase, cancellationToken);

        var newSnapshots = items
            .Where(item => !alreadyCaptured.Contains(item.MarketHashName))
            .Select(item => new ItemPriceSnapshot
            {
                MarketHashName = item.MarketHashName,
                Currency = currency,
                CapturedDate = today,
                Price = item.Price,
                Quantity = item.Quantity
            })
            .ToList();

        if (newSnapshots.Count == 0)
        {
            return 0;
        }

        dbContext.ItemPriceSnapshots.AddRange(newSnapshots);
        await dbContext.SaveChangesAsync(cancellationToken);
        return newSnapshots.Count;
    }

    public Task<DateOnly?> GetLastDailySnapshotDateAsync(string currency, CancellationToken cancellationToken) =>
        dbContext.ItemPriceSnapshots
            .Where(snapshot => snapshot.Currency == currency && snapshot.Source == "skinport")
            .MaxAsync(snapshot => (DateOnly?)snapshot.CapturedDate, cancellationToken);

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
                snapshot.Price,
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
                Price = period.Price,
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
            ConsecutiveBulkFailuresByItem.TryRemove(marketHashName, out _);
            return new BulkBackfillItemResult(Imported: 0, AlreadyCovered: true);
        }

        await WaitForSteamThrottleAsync(cancellationToken);

        try
        {
            var imported = await BackfillFromSteamAsync(marketHashName, cancellationToken);
            if (imported is null)
            {
                // Rate-limited - not this item's fault, don't count it against it.
                return new BulkBackfillItemResult(Imported: 0, AlreadyCovered: false);
            }

            ConsecutiveBulkFailuresByItem.TryRemove(marketHashName, out _);
            return new BulkBackfillItemResult(Imported: imported.Value, AlreadyCovered: false);
        }
        catch (Exception ex)
        {
            RegisterSteamCallError(marketHashName);

            var failures = ConsecutiveBulkFailuresByItem.AddOrUpdate(marketHashName, 1, (_, count) => count + 1);
            if (failures >= MaxConsecutiveBulkFailuresBeforeGivingUp)
            {
                logger.LogWarning(
                    ex,
                    "Bulk backfill for {MarketHashName} failed {Failures} times in a row; marking it as no-data so it stops blocking the batch queue.",
                    marketHashName, failures);
                await MarkNoSteamDataAsync(marketHashName, cancellationToken);
                ConsecutiveBulkFailuresByItem.TryRemove(marketHashName, out _);
            }
            else
            {
                logger.LogWarning(ex, "Bulk backfill for {MarketHashName} failed ({Failures}/{Max}).", marketHashName, failures, MaxConsecutiveBulkFailuresBeforeGivingUp);
            }

            return new BulkBackfillItemResult(Imported: 0, AlreadyCovered: false);
        }
    }

    // One query for the whole catalog instead of one per item - lets a batch
    // endpoint pick its next N items without an N+1 coverage check
    public async Task<IReadOnlyList<string>> GetNextUncoveredItemsAsync(
        string currency,
        IReadOnlyList<string> candidateMarketHashNames,
        int count,
        CancellationToken cancellationToken)
    {
        // Steam returns an item's full available history in one response (no
        // pagination), so one successful fetch is complete - requiring 85+
        // days of depth here would leave newer items (with genuinely less
        // history than that) looking "uncovered" forever and stuck retrying
        var coveredNames = await dbContext.ItemPriceSnapshots
            .Where(snapshot =>
                (snapshot.Currency == currency && snapshot.Source == "steam") ||
                snapshot.Source == NoSteamDataSource)
            .Select(snapshot => snapshot.MarketHashName)
            .ToHashSetAsync(StringComparer.OrdinalIgnoreCase, cancellationToken);

        return candidateMarketHashNames
            .Where(name => !coveredNames.Contains(name))
            .Take(count)
            .ToList();
    }

    // Marks an item Steam confirmed has no sale history - currency-agnostic
    // (that fact doesn't depend on currency), so batch/reactive backfill
    // stop retrying it instead of hitting Steam for it forever
    private async Task MarkNoSteamDataAsync(string marketHashName, CancellationToken cancellationToken)
    {
        var alreadyMarked = await dbContext.ItemPriceSnapshots.AnyAsync(
            snapshot => snapshot.MarketHashName == marketHashName && snapshot.Source == NoSteamDataSource,
            cancellationToken);
        if (alreadyMarked)
        {
            return;
        }

        dbContext.ItemPriceSnapshots.Add(new ItemPriceSnapshot
        {
            MarketHashName = marketHashName,
            Currency = "N/A",
            CapturedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Quantity = 0,
            Source = NoSteamDataSource
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // A "steam" row counts as real coverage, and so does a confirmed
    // no-data marker - a single Skinport seed anchor would otherwise look
    // "covered" forever and block the real backfill
    private async Task<bool> HasSufficientCoverageAsync(
        string marketHashName,
        string currency,
        CancellationToken cancellationToken)
    {
        var hasNoDataMarker = await dbContext.ItemPriceSnapshots.AnyAsync(
            snapshot => snapshot.MarketHashName == marketHashName && snapshot.Source == NoSteamDataSource,
            cancellationToken);
        if (hasNoDataMarker)
        {
            return true;
        }

        // Same reasoning as GetNextUncoveredItemsAsync: one successful fetch
        // is complete, Steam doesn't hand back more on a second call
        return await dbContext.ItemPriceSnapshots.AnyAsync(
            snapshot => snapshot.MarketHashName == marketHashName &&
                snapshot.Currency == currency &&
                snapshot.Source == "steam",
            cancellationToken);
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
            // Common for stickers/agents/graffiti - mark it so batch backfill
            // stops retrying an item Steam will never have history for
            await MarkNoSteamDataAsync(marketHashName, cancellationToken);
            return 0;
        }

        var data = result.Data;
        if (data.Points.Count == 0)
        {
            await MarkNoSteamDataAsync(marketHashName, cancellationToken);
            return 0;
        }

        // checks, that only the last 90 days are imported + ensures, that nothing is imported twice
        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-BackfillTargetDays);
        var relevantPoints = data.Points.Where(point => point.Date >= cutoff).ToList();
        if (relevantPoints.Count == 0)
        {
            // Steam has history, just none of it recent enough to matter -
            // same deal as NoData: nothing will change on a retry, so mark
            // it or GetNextUncoveredItemsAsync picks it again forever
            await MarkNoSteamDataAsync(marketHashName, cancellationToken);
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
                Price = point.Price,
                Quantity = point.Volume,
                Source = "steam"
            });
            imported++;
        }

        if (imported > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        else
        {
            // Steam had points, but every date is already taken by a
            // non-steam row (daily Skinport snapshot / period seed) - the
            // unique (name, currency, date) index means a "steam" row can
            // never land on those dates either. Nothing will change on a
            // retry, so mark it like the no-data case or GetNextUncoveredItemsAsync
            // picks it again forever.
            await MarkNoSteamDataAsync(marketHashName, cancellationToken);
        }

        return imported;
    }
}

public sealed record BulkBackfillItemResult(int Imported, bool AlreadyCovered);
