namespace LootBase.Application.Abstractions.Pricing;

public sealed record SteamMarketDailyPricePoint(DateOnly Date, decimal MedianPrice, int Volume);

public sealed record SteamMarketHistoryDto(string Currency, IReadOnlyList<SteamMarketDailyPricePoint> Points);

public enum SteamMarketHistoryOutcome
{
    Success,

    // The call completed but Steam has no market history for this item -
    // very common for stickers/agents/graffiti/patches that aren't
    // individually listed. Not a sign of rate limiting.
    NoData,

    // Steam responded 429 - an explicit, unambiguous signal to back off.
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
