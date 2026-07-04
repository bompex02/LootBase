using LootBase.Application.Abstractions.Inventory;
using LootBase.Application.Abstractions.Pricing;
using LootBase.Application.Players;
using LootBase.Domain.Inventory;
using LootBase.Domain.Users;
using LootBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LootBase.Infrastructure.Inventory;

public sealed class InventoryRefreshService(
    LootBaseDbContext dbContext,
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

        var user = await dbContext.Users
            .FirstOrDefaultAsync(candidate => candidate.SteamId64 == steamId64, cancellationToken);

        if (user is null)
        {
            user = new User
            {
                SteamId64 = steamId64,
                PersonaName = $"Steam {steamId64[^6..]}"
            };

            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var existingItems = await dbContext.InventoryItems
            .Where(item => item.UserId == user.Id && item.AppId == appId)
            .ToListAsync(cancellationToken);
        var existingItemsByAssetId = existingItems.ToDictionary(item => item.AssetId);

        var inventory = await provider.GetInventoryAsync(steamId64, cancellationToken);
        var refreshedItems = new List<InventoryItem>();
        var pricesByMarketHashName = new Dictionary<string, PriceQuote?>(
            StringComparer.Ordinal);
        foreach (var asset in inventory)
        {
            if (!pricesByMarketHashName.TryGetValue(asset.MarketHashName, out var price))
            {
                price = await pricingProvider.GetPriceAsync(asset.MarketHashName, "EUR", cancellationToken);
                pricesByMarketHashName[asset.MarketHashName] = price;
            }

            if (!existingItemsByAssetId.TryGetValue(asset.AssetId, out var item))
            {
                item = new InventoryItem
                {
                    UserId = user.Id,
                    AppId = appId,
                    AssetId = asset.AssetId,
                    ClassId = asset.ClassId,
                    MarketHashName = asset.MarketHashName,
                    DisplayName = asset.DisplayName
                };

                dbContext.InventoryItems.Add(item);
            }

            item.ClassId = asset.ClassId;
            item.InstanceId = asset.InstanceId;
            item.MarketHashName = asset.MarketHashName;
            item.DisplayName = asset.DisplayName;
            item.IconUrl = asset.IconUrl;
            item.Type = asset.Type;
            item.Exterior = asset.Exterior;
            item.Rarity = asset.Rarity;
            item.Quantity = Math.Max(1, asset.Quantity);
            item.UnitPrice = price?.Amount ?? 0;
            item.Currency = price?.Currency ?? "EUR";
            item.LastPricedAt = price?.PricedAt;

            refreshedItems.Add(item);
        }

        user.LastInventoryRefreshAt = DateTimeOffset.UtcNow;
        dbContext.InventorySnapshots.Add(new InventorySnapshot
        {
            UserId = user.Id,
            AppId = appId,
            TotalValue = refreshedItems.Sum(item => item.TotalPrice),
            Currency = "EUR",
            CapturedAt = user.LastInventoryRefreshAt.Value
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return await playerProfiles.GetAsync(steamId64, appId, cancellationToken)
            ?? throw new InvalidOperationException("Player profile disappeared after inventory refresh.");
    }
}
