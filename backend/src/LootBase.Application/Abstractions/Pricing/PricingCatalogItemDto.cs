namespace LootBase.Application.Abstractions.Pricing;

// Price is the average price over the source window, not a single quote
public sealed record PricingCatalogItemDto(
    string MarketHashName,
    string Currency,
    decimal? Price,
    string? ItemPage,
    string? MarketPage,
    string Source,
    DateTimeOffset RetrievedAt);
