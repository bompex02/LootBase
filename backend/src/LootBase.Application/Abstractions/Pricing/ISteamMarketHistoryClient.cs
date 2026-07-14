namespace LootBase.Application.Abstractions.Pricing;

public sealed record SteamMarketDailyPricePoint(DateOnly Date, decimal MedianPrice, int Volume);

public sealed record SteamMarketHistoryDto(string Currency, IReadOnlyList<SteamMarketDailyPricePoint> Points);

public interface ISteamMarketHistoryClient
{
    Task<SteamMarketHistoryDto?> GetPriceHistoryAsync(string marketHashName, CancellationToken cancellationToken);
}
