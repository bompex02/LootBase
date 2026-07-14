namespace LootBase.Application.Abstractions.Pricing;

public sealed record PricingHistoryPeriodDto(
    string Period,
    decimal? MinPrice,
    decimal? MaxPrice,
    decimal? AvgPrice,
    decimal? MedianPrice,
    int Volume);

public sealed record PricingHistoryDailyPointDto(
    DateOnly Date,
    decimal? MinPrice,
    decimal? MaxPrice,
    decimal? AvgPrice,
    decimal? MedianPrice,
    int Quantity);

public sealed record PricingHistoryDto(
    string MarketHashName,
    string Currency,
    IReadOnlyList<PricingHistoryPeriodDto> Periods,
    IReadOnlyList<PricingHistoryDailyPointDto> DailyPoints);
