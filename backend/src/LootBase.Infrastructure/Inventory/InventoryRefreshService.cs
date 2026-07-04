using LootBase.Application.Abstractions.Inventory;
using LootBase.Application.Abstractions.Persistence;
using LootBase.Application.Abstractions.Pricing;
using LootBase.Application.Players;
using LootBase.Domain.Inventory;

namespace LootBase.Infrastructure.Inventory;

public sealed class InventoryRefreshService(
    IUserRepository users,
    IEnumerable<IInventoryProvider> inventoryProviders,
    IPricingProvider pricingProvider,
    IPlayerProfileService playerProfiles) : IInventoryRefreshService
{
    public async Task<PlayerProfileDto> RefreshAsync(
        string steamId64,
        int appId,
        CancellationToken cancellationToken)
    {
        var provider = inventoryProviders.FirstOrDefault(x => x.AppId == appId)
            ?? throw new InvalidOperationException($"No inventory provider registered for appid {appId}.");

        var user = await users.GetBySteamIdAsync(steamId64, cancellationToken)
            ?? await users.UpsertSteamUserAsync(steamId64, $"Steam {steamId64[^6..]}", null, cancellationToken);

        var existingItems = user.InventoryItems.Where(item => item.AppId == appId).ToList();
        foreach (var item in existingItems)
        {
            user.InventoryItems.Remove(item);
        }

        var inventory = await provider.GetInventoryAsync(steamId64, cancellationToken);
        foreach (var asset in inventory)
        {
            var price = await pricingProvider.GetPriceAsync(asset.MarketHashName, "EUR", cancellationToken);
            user.InventoryItems.Add(new InventoryItem
            {
                AppId = appId,
                AssetId = asset.AssetId,
                ClassId = asset.ClassId,
                InstanceId = asset.InstanceId,
                MarketHashName = asset.MarketHashName,
                DisplayName = asset.DisplayName,
                IconUrl = asset.IconUrl,
                Type = asset.Type,
                Exterior = asset.Exterior,
                Rarity = asset.Rarity,
                Quantity = Math.Max(1, asset.Quantity),
                UnitPrice = price?.Amount ?? 0,
                Currency = price?.Currency ?? "EUR",
                LastPricedAt = price?.PricedAt
            });
        }

        user.LastInventoryRefreshAt = DateTimeOffset.UtcNow;
        user.InventorySnapshots.Add(new InventorySnapshot
        {
            AppId = appId,
            TotalValue = user.InventoryItems.Where(item => item.AppId == appId).Sum(item => item.TotalPrice),
            Currency = "EUR",
            CapturedAt = user.LastInventoryRefreshAt.Value
        });

        await users.SaveChangesAsync(cancellationToken);

        return await playerProfiles.GetAsync(steamId64, appId, cancellationToken)
            ?? throw new InvalidOperationException("Player profile disappeared after inventory refresh.");
    }
}
