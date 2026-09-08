namespace LootBase.Application.Abstractions.Pricing;

// Price is the average price over the period/day, not a single quote
public sealed record PricingHistoryPeriodDto(
    string Period,
    decimal? Price,
    int Volume);

// Price is the average price over that day, not a single quote
public sealed record PricingHistoryDailyPointDto(
    DateOnly Date,
    decimal? Price,
    int Quantity);

public sealed record PricingHistoryDto(
    string MarketHashName,
    string Currency,
    IReadOnlyList<PricingHistoryPeriodDto> Periods,
    IReadOnlyList<PricingHistoryDailyPointDto> DailyPoints);

// One item's input for the daily snapshot-all batch (see IPricingHistoryProvider.SnapshotAllAsync)
// Price is the average price over the source window, not a single quote
public sealed record DailySnapshotItemDto(
    string MarketHashName,
    decimal? Price,
    int Quantity);
