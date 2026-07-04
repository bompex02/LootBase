using LootBase.Application.Abstractions.Persistence;

namespace LootBase.Application.Players;

public sealed class PlayerProfileService(IUserRepository users) : IPlayerProfileService
{
    public async Task<PlayerProfileDto?> GetAsync(string steamId64, int appId, CancellationToken cancellationToken)
    {
        var user = await users.GetBySteamIdAsync(steamId64, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var items = user.InventoryItems
            .Where(item => item.AppId == appId)
            .OrderByDescending(item => item.TotalPrice)
            .ToList();

        var currency = items.FirstOrDefault()?.Currency ?? "EUR";
        var topItems = items
            .Take(20)
            .Select(item => new InventoryItemDto(
                item.AssetId,
                item.MarketHashName,
                item.DisplayName,
                item.IconUrl,
                item.Type,
                item.Exterior,
                item.Rarity,
                item.Quantity,
                item.UnitPrice,
                item.TotalPrice,
                item.Currency))
            .ToList();

        return new PlayerProfileDto(
            user.SteamId64,
            user.PersonaName,
            user.AvatarUrl,
            items.Sum(item => item.TotalPrice),
            currency,
            items.Sum(item => item.Quantity),
            user.LastInventoryRefreshAt,
            topItems);
    }
}
