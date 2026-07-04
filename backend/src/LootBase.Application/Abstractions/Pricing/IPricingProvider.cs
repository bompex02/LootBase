namespace LootBase.Application.Abstractions.Pricing;

public interface IPricingProvider
{
    Task<PriceQuote?> GetPriceAsync(string marketHashName, string currency, CancellationToken cancellationToken);
}
