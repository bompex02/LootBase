namespace LootBase.Application.Abstractions.Pricing;

public interface IPricingHistoryProvider
{
    Task<PricingHistoryDto?> GetHistoryAsync(
        string marketHashName,
        string currency,
        CancellationToken cancellationToken);

    // Null means the Steam call itself failed; 0+ is the number imported
    Task<int?> BackfillFromSteamAsync(
        string marketHashName,
        CancellationToken cancellationToken);

    // Backfills every Skinport-known item from Steam. Can take hours - run
    // it fire-and-forget, not on a request path
    Task BackfillAllFromSteamAsync(string currency, CancellationToken cancellationToken);

    // Writes today's snapshot for every item in the Skinport catalog in one
    // batch (a handful of DB round trips, not one per item) so history stays
    // gapless regardless of which items got page views today. Fast enough to
    // run synchronously within a single request; meant to be hit by an
    // external daily scheduler. Returns the number of rows written.
    Task<int> SnapshotAllAsync(string currency, CancellationToken cancellationToken);

    // Snapshot of the current (or last) bulk run. Resets on restart
    BulkBackfillStatusDto GetBulkBackfillStatus();

    // Claims the "running" slot so two bulk runs can't overlap. Call before
    // BackfillAllFromSteamAsync; false means one's already in progress
    bool TryStartBulkBackfill();
}
