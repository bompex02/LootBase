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

    // Snapshot of the current (or last) bulk run. Resets on restart
    BulkBackfillStatusDto GetBulkBackfillStatus();

    // Claims the "running" slot so two bulk runs can't overlap. Call before
    // BackfillAllFromSteamAsync; false means one's already in progress
    bool TryStartBulkBackfill();
}
