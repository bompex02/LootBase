namespace LootBase.Application.Abstractions.Pricing;

public interface IPricingHistoryProvider
{
    Task<PricingHistoryDto?> GetHistoryAsync(
        string marketHashName,
        string currency,
        CancellationToken cancellationToken);

    Task<int> BackfillFromSteamAsync(
        string marketHashName,
        CancellationToken cancellationToken);
}
