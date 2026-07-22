namespace LootBase.Application.Abstractions.Pricing;

public interface IPricingHistoryProvider
{
    Task<PricingHistoryDto?> GetHistoryAsync(
        string marketHashName,
        string currency,
        CancellationToken cancellationToken);

    // Null means the Steam call itself failed; 0+ is the number imported.
    Task<int?> BackfillFromSteamAsync(
        string marketHashName,
        CancellationToken cancellationToken);

    // Walks every item Skinport currently lists and backfills Steam history
    // for each one, respecting the shared global throttle/circuit breaker.
    // A full run can take hours - call this fire-and-forget from a
    // background task, not on a request path. Progress and the final
    // summary go to the logs.
    Task BackfillAllFromSteamAsync(string currency, CancellationToken cancellationToken);

    // In-memory snapshot of the most recent (or currently running)
    // BackfillAllFromSteamAsync call. Resets on restart, same as the
    // throttle/circuit-breaker state.
    BulkBackfillStatusDto GetBulkBackfillStatus();

    // Atomically claims the "running" slot so two overlapping triggers
    // can't both start a bulk run. Call this synchronously before
    // scheduling BackfillAllFromSteamAsync; false means one is already
    // in progress.
    bool TryStartBulkBackfill();
}
