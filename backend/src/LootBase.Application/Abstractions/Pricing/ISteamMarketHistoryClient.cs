namespace LootBase.Application.Abstractions.Pricing;

// Price is the average of that day's sales, not a single quote
public sealed record SteamMarketDailyPricePoint(DateOnly Date, decimal Price, int Volume);

public sealed record SteamMarketHistoryDto(string Currency, IReadOnlyList<SteamMarketDailyPricePoint> Points);

public enum SteamMarketHistoryOutcome
{
    Success,

    // No history for this item (common for stickers/agents/graffiti). Not rate limiting
    NoData,

    // Steam responded 429 - an explicit, unambiguous signal to back off
    RateLimited
}

public sealed record SteamMarketHistoryResult(SteamMarketHistoryOutcome Outcome, SteamMarketHistoryDto? Data)
{
    public static readonly SteamMarketHistoryResult NoDataResult = new(SteamMarketHistoryOutcome.NoData, null);
    public static readonly SteamMarketHistoryResult RateLimitedResult = new(SteamMarketHistoryOutcome.RateLimited, null);

    public static SteamMarketHistoryResult Success(SteamMarketHistoryDto data) =>
        new(SteamMarketHistoryOutcome.Success, data);
}

public interface ISteamMarketHistoryClient
{
    Task<SteamMarketHistoryResult> GetPriceHistoryAsync(string marketHashName, CancellationToken cancellationToken);
}
