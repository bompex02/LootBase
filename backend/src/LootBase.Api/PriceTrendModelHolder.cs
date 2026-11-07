using LootBase.PricePrediction;

namespace LootBase.Api;

// Always registered; Model stays null until the startup load below finds a
// row - keeps the endpoint from dealing with "service not registered" at all
public sealed class PriceTrendModelHolder
{
    public PriceTrendModel? Model { get; set; }
}
