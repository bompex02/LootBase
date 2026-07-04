namespace LootBase.Application.Abstractions.Pricing;

public sealed record PricingCatalogItemDto(
    string MarketHashName,
    string Currency,
    decimal? SuggestedPrice,
    decimal? MeanPrice,
    decimal? MedianPrice,
    decimal? MinPrice,
    decimal? MaxPrice,
    int Quantity,
    string? ItemPage,
    string? MarketPage,
    string Source,
    DateTimeOffset RetrievedAt,
    DateTimeOffset? UpdatedAt);
