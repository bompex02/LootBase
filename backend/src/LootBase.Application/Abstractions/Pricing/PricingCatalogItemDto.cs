namespace LootBase.Application.Abstractions.Pricing;

public sealed record PricingCatalogItemDto(
    string MarketHashName,
    string Currency,
    decimal? MeanPrice,
    decimal? MedianPrice,
    decimal? MinPrice,
    decimal? MaxPrice,
    string? ItemPage,
    string? MarketPage,
    string Source,
    DateTimeOffset RetrievedAt);
