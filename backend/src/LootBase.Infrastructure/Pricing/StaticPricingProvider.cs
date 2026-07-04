using LootBase.Application.Abstractions.Pricing;

namespace LootBase.Infrastructure.Pricing;

public sealed class StaticPricingProvider : IPricingProvider
{
    private static readonly IReadOnlyDictionary<string, decimal> Prices = new Dictionary<string, decimal>
    {
        ["AK-47 | Redline (Field-Tested)"] = 37.21m,
        ["AWP | Asiimov (Battle-Scarred)"] = 118.42m,
        ["Sport Gloves | Vice (Field-Tested)"] = 6120.00m
    };

    public Task<PriceQuote?> GetPriceAsync(
        string marketHashName,
        string currency,
        CancellationToken cancellationToken)
    {
        if (!Prices.TryGetValue(marketHashName, out var amount))
        {
            return Task.FromResult<PriceQuote?>(null);
        }

        return Task.FromResult<PriceQuote?>(
            new PriceQuote(marketHashName, amount, currency, "static-demo", DateTimeOffset.UtcNow));
    }
}
