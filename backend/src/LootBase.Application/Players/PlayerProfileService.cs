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
            .GroupBy(item => new { item.MarketHashName, item.Currency })
            .Select(group =>
            {
                var representative = group.First();
                var quantity = group.Sum(item => item.Quantity);
                var totalPrice = group.Sum(item => item.TotalPrice);

                return new InventoryItemDto(
                    representative.AssetId,
                    representative.MarketHashName,
                    representative.DisplayName,
                    representative.IconUrl,
                    representative.Type,
                    representative.Exterior,
                    representative.Rarity,
                    quantity,
                    quantity == 0 ? 0 : totalPrice / quantity,
                    totalPrice,
                    representative.Currency);
            })
            .OrderByDescending(item => item.TotalPrice)
            .Take(20)
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
