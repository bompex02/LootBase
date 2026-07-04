namespace LootBase.Application.Abstractions.Pricing;

public interface IPricingCatalog : IPricingProvider
{
    Task<IReadOnlyList<PricingCatalogItemDto>> GetItemsAsync(
        IReadOnlyCollection<string> marketHashNames,
        string currency,
        CancellationToken cancellationToken);

    Task<PricingCatalogItemDto?> GetItemAsync(
        string marketHashName,
        string currency,
        CancellationToken cancellationToken);
}
