using LootBase.Application.Abstractions.Inventory;
using LootBase.Domain.Games;

namespace LootBase.Infrastructure.Inventory;

public sealed class Cs2DemoInventoryProvider : IInventoryProvider
{
    public int AppId => SteamGames.CounterStrike2;

    public Task<IReadOnlyCollection<InventoryAsset>> GetInventoryAsync(
        string steamId64,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<InventoryAsset> items =
        [
            new(
                $"{steamId64}-ak-redline",
                "310777",
                "0",
                "AK-47 | Redline (Field-Tested)",
                "AK-47 | Redline",
                null,
                "Rifle",
                "Field-Tested",
                "Classified",
                1),
            new(
                $"{steamId64}-awp-asiimov",
                "320111",
                "0",
                "AWP | Asiimov (Battle-Scarred)",
                "AWP | Asiimov",
                null,
                "Sniper Rifle",
                "Battle-Scarred",
                "Covert",
                1),
            new(
                $"{steamId64}-gloves-vice",
                "401921",
                "0",
                "Sport Gloves | Vice (Field-Tested)",
                "Sport Gloves | Vice",
                null,
                "Gloves",
                "Field-Tested",
                "Extraordinary",
                1)
        ];

        return Task.FromResult(items);
    }
}
