using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using LootBase.Application.Abstractions.Pricing;

namespace LootBase.Infrastructure.Pricing;

public sealed class SteamMarketPricingProvider(HttpClient httpClient) : IPricingProvider
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);
    private static readonly ConcurrentDictionary<string, CachedPrice> Cache = new();

    public async Task<PriceQuote?> GetPriceAsync(
        string marketHashName,
        string currency,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"{currency}:{marketHashName}";
        if (Cache.TryGetValue(cacheKey, out var cachedPrice) &&
            cachedPrice.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cachedPrice.Price;
        }

        var currencyCode = GetSteamCurrencyCode(currency);
        var requestUri =
            $"https://steamcommunity.com/market/priceoverview/?appid=730&currency={currencyCode}&market_hash_name={Uri.EscapeDataString(marketHashName)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.UserAgent.ParseAdd("LootBase/0.1");
        request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            Cache[cacheKey] = new CachedPrice(null, DateTimeOffset.UtcNow.AddMinutes(2));
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        if (root.TryGetProperty("success", out var successElement) &&
            successElement.ValueKind == JsonValueKind.False)
        {
            Cache[cacheKey] = new CachedPrice(null, DateTimeOffset.UtcNow.AddMinutes(2));
            return null;
        }

        var priceText = ReadOptionalString(root, "lowest_price") ??
            ReadOptionalString(root, "median_price");
        if (!TryParseSteamPrice(priceText, out var amount))
        {
            Cache[cacheKey] = new CachedPrice(null, DateTimeOffset.UtcNow.AddMinutes(2));
            return null;
        }

        var quote = new PriceQuote(
            marketHashName,
            amount,
            currency.ToUpperInvariant(),
            "steam-community-market",
            DateTimeOffset.UtcNow);
        Cache[cacheKey] = new CachedPrice(quote, DateTimeOffset.UtcNow.Add(CacheDuration));

        return quote;
    }

    private static int GetSteamCurrencyCode(string currency)
    {
        return currency.Equals("EUR", StringComparison.OrdinalIgnoreCase) ? 3 : 1;
    }

    private static bool TryParseSteamPrice(string? priceText, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(priceText))
        {
            return false;
        }

        var numericBuilder = new StringBuilder();
        foreach (var character in priceText)
        {
            if (char.IsDigit(character) || character is ',' or '.')
            {
                numericBuilder.Append(character);
            }
        }

        var numericText = numericBuilder.ToString();
        if (string.IsNullOrWhiteSpace(numericText))
        {
            return false;
        }

        var lastComma = numericText.LastIndexOf(',');
        var lastDot = numericText.LastIndexOf('.');
        var decimalSeparator = lastComma > lastDot ? ',' : '.';
        var thousandsSeparator = decimalSeparator == ',' ? "." : ",";

        if (lastComma >= 0 || lastDot >= 0)
        {
            numericText = numericText.Replace(thousandsSeparator, string.Empty);
            numericText = numericText.Replace(decimalSeparator, '.');
        }

        return decimal.TryParse(
            numericText,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out amount);
    }

    private static string? ReadOptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private sealed record CachedPrice(PriceQuote? Price, DateTimeOffset ExpiresAt);
}
