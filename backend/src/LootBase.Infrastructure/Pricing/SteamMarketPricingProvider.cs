using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using LootBase.Application.Abstractions.Pricing;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace LootBase.Infrastructure.Pricing;

public sealed class SteamMarketPricingProvider(
    HttpClient httpClient,
    IDistributedCache distributedCache,
    ILogger<SteamMarketPricingProvider> logger) : IPricingProvider
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan MissingPriceCacheDuration = TimeSpan.FromMinutes(2);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly ConcurrentDictionary<string, CachedPrice> MemoryCache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<PriceQuote?> GetPriceAsync(
        string marketHashName,
        string currency,
        CancellationToken cancellationToken)
    {
        var normalizedCurrency = currency.ToUpperInvariant();
        var cacheKey = GetCacheKey(marketHashName, normalizedCurrency);
        if (MemoryCache.TryGetValue(cacheKey, out var cachedPrice) &&
            cachedPrice.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cachedPrice.Price;
        }

        var distributedPrice = await ReadDistributedCacheAsync(cacheKey, cancellationToken);
        if (distributedPrice.Found)
        {
            MemoryCache[cacheKey] = new CachedPrice(
                distributedPrice.Price,
                DateTimeOffset.UtcNow.Add(distributedPrice.Price is null
                    ? MissingPriceCacheDuration
                    : CacheDuration));

            return distributedPrice.Price;
        }

        var currencyCode = GetSteamCurrencyCode(normalizedCurrency);
        var requestUri =
            $"https://steamcommunity.com/market/priceoverview/?appid=730&currency={currencyCode}&market_hash_name={Uri.EscapeDataString(marketHashName)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.UserAgent.ParseAdd("LootBase/0.1");
        request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Steam Market pricing request failed with status code {StatusCode} for {MarketHashName}.",
                (int)response.StatusCode,
                marketHashName);

            await CachePriceAsync(cacheKey, null, MissingPriceCacheDuration, cancellationToken);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        if (root.TryGetProperty("success", out var successElement) &&
            successElement.ValueKind == JsonValueKind.False)
        {
            await CachePriceAsync(cacheKey, null, MissingPriceCacheDuration, cancellationToken);
            return null;
        }

        var priceText = ReadOptionalString(root, "lowest_price") ??
            ReadOptionalString(root, "median_price");
        if (!TryParseSteamPrice(priceText, out var amount))
        {
            await CachePriceAsync(cacheKey, null, MissingPriceCacheDuration, cancellationToken);
            return null;
        }

        var quote = new PriceQuote(
            marketHashName,
            amount,
            normalizedCurrency,
            "steam-community-market",
            DateTimeOffset.UtcNow);

        await CachePriceAsync(cacheKey, quote, CacheDuration, cancellationToken);

        return quote;
    }

    private async Task<(bool Found, PriceQuote? Price)> ReadDistributedCacheAsync(
        string cacheKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var cachedJson = await distributedCache.GetStringAsync(cacheKey, cancellationToken);
            if (string.IsNullOrWhiteSpace(cachedJson))
            {
                return (false, null);
            }

            var cachedPrice = JsonSerializer.Deserialize<CachedPriceDto>(cachedJson, JsonOptions);
            if (cachedPrice is null)
            {
                return (false, null);
            }

            if (!cachedPrice.HasPrice)
            {
                return (true, null);
            }

            return (true, new PriceQuote(
                cachedPrice.MarketHashName,
                cachedPrice.Amount,
                cachedPrice.Currency,
                cachedPrice.Source,
                cachedPrice.PricedAt));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Reading Steam Market price from distributed cache failed.");
            return (false, null);
        }
    }

    private async Task CachePriceAsync(
        string cacheKey,
        PriceQuote? price,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        MemoryCache[cacheKey] = new CachedPrice(price, DateTimeOffset.UtcNow.Add(duration));

        try
        {
            var cachedPrice = price is null
                ? CachedPriceDto.Missing
                : new CachedPriceDto(
                    true,
                    price.MarketHashName,
                    price.Amount,
                    price.Currency,
                    price.Source,
                    price.PricedAt);

            var cachedJson = JsonSerializer.Serialize(cachedPrice, JsonOptions);
            await distributedCache.SetStringAsync(
                cacheKey,
                cachedJson,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = duration
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Writing Steam Market price to distributed cache failed.");
        }
    }

    private static string GetCacheKey(string marketHashName, string currency)
    {
        return $"pricing:steam-market:730:{currency}:{Convert.ToHexString(Encoding.UTF8.GetBytes(marketHashName)).ToLowerInvariant()}:v1";
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

    private sealed record CachedPriceDto(
        bool HasPrice,
        string MarketHashName,
        decimal Amount,
        string Currency,
        string Source,
        DateTimeOffset PricedAt)
    {
        public static CachedPriceDto Missing { get; } = new(
            false,
            string.Empty,
            0,
            string.Empty,
            string.Empty,
            DateTimeOffset.UnixEpoch);
    }
}
