using System.Globalization;
using System.Text.Json;
using LootBase.Application.Abstractions.Pricing;
using LootBase.Infrastructure.Auth.Steam;
using Microsoft.Extensions.Logging;

namespace LootBase.Infrastructure.Pricing;

// Steam's pricehistory endpoint is unofficial and needs a logged-in session
// cookie, which SteamAccessTokenProvider mints on demand from a refresh token
public sealed class SteamMarketHistoryClient(
    HttpClient httpClient,
    SteamAccessTokenProvider accessTokenProvider,
    ILogger<SteamMarketHistoryClient> logger) : ISteamMarketHistoryClient
{
    private static readonly Dictionary<string, string> CurrencySymbols = new()
    {
        ["€"] = "EUR",
        ["$"] = "USD",
        ["£"] = "GBP",
        ["pуб"] = "RUB"
    };

    public async Task<SteamMarketHistoryResult> GetPriceHistoryAsync(
        string marketHashName,
        CancellationToken cancellationToken)
    {
        var cookie = await accessTokenProvider.GetMarketCookieAsync(cancellationToken);
        if (cookie is null)
        {
            logger.LogWarning("Steam:MarketRefreshToken is not configured or refresh failed; cannot fetch Steam market price history.");
            return SteamMarketHistoryResult.NoDataResult;
        }

        var requestUri =
            $"https://steamcommunity.com/market/pricehistory/?appid=730&market_hash_name={Uri.EscapeDataString(marketHashName)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Add("Cookie", cookie);

        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            logger.LogWarning("Steam market price history request was rate limited (429).");
            return SteamMarketHistoryResult.RateLimitedResult;
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogDebug(
                "Steam market price history request for {MarketHashName} returned {StatusCode}; treating as no data.",
                marketHashName, (int)response.StatusCode);
            return SteamMarketHistoryResult.NoDataResult;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        if (!root.TryGetProperty("success", out var successElement) ||
            successElement.ValueKind != JsonValueKind.True ||
            !root.TryGetProperty("prices", out var pricesElement))
        {
            return SteamMarketHistoryResult.NoDataResult;
        }

        var currency = DetectCurrency(root);
        var points = ParsePoints(pricesElement);

        return SteamMarketHistoryResult.Success(new SteamMarketHistoryDto(currency, points));
    }

    private static string DetectCurrency(JsonElement root)
    {
        var suffix = root.TryGetProperty("price_suffix", out var suffixElement) ? suffixElement.GetString() : null;
        var prefix = root.TryGetProperty("price_prefix", out var prefixElement) ? prefixElement.GetString() : null;
        var symbol = (suffix ?? prefix ?? string.Empty).Trim();

        foreach (var (candidateSymbol, code) in CurrencySymbols)
        {
            if (symbol.Contains(candidateSymbol, StringComparison.Ordinal))
            {
                return code;
            }
        }

        return "EUR";
    }

    private static IReadOnlyList<SteamMarketDailyPricePoint> ParsePoints(JsonElement pricesElement)
    {
        var byDate = new Dictionary<DateOnly, (decimal PriceSum, int PriceCount, int Volume)>();

        foreach (var entry in pricesElement.EnumerateArray())
        {
            if (entry.GetArrayLength() < 3)
            {
                continue;
            }

            var date = ParseSteamDate(entry[0].GetString());
            if (date is null)
            {
                continue;
            }

            var price = entry[1].GetDecimal();
            var volume = int.TryParse(entry[2].GetString(), out var parsedVolume) ? parsedVolume : 0;

            byDate[date.Value] = byDate.TryGetValue(date.Value, out var existing)
                ? (existing.PriceSum + price, existing.PriceCount + 1, existing.Volume + volume)
                : (price, 1, volume);
        }

        return byDate
            .Select(pair => new SteamMarketDailyPricePoint(
                pair.Key,
                Math.Round(pair.Value.PriceSum / pair.Value.PriceCount, 2),
                pair.Value.Volume))
            .OrderBy(point => point.Date)
            .ToList();
    }

    private static DateOnly? ParseSteamDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var datePart = raw.Split(':')[0].Trim();
        var tokens = datePart.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 3)
        {
            return null;
        }

        var candidate = $"{tokens[0]} {tokens[1]} {tokens[2]}";
        return DateOnly.TryParseExact(
            candidate, "MMM d yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }
}
