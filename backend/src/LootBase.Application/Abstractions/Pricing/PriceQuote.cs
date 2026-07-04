namespace LootBase.Application.Abstractions.Pricing;

public sealed record PriceQuote(
    string MarketHashName,
    decimal Amount,
    string Currency,
    string Source,
    DateTimeOffset PricedAt);
