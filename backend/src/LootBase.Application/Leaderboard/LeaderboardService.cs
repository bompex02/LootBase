using LootBase.Application.Abstractions.Persistence;

namespace LootBase.Application.Leaderboard;

public sealed class LeaderboardService(IUserRepository users) : ILeaderboardService
{
    public async Task<IReadOnlyList<LeaderboardEntryDto>> GetAsync(
        int appId,
        int limit,
        CancellationToken cancellationToken)
    {
        var rankedUsers = (await users.GetUsersWithInventoriesAsync(appId, cancellationToken))
            .Select(user =>
            {
                var inventory = user.InventoryItems.Where(item => item.AppId == appId).ToList();
                var currency = inventory.FirstOrDefault()?.Currency ?? "EUR";

                return new
                {
                    User = user,
                    Currency = currency,
                    ItemCount = inventory.Sum(item => item.Quantity),
                    Value = inventory.Sum(item => item.TotalPrice)
                };
            })
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.User.PersonaName)
            .Take(Math.Max(1, limit))
            .ToList();

        return rankedUsers
            .Select((entry, index) => new LeaderboardEntryDto(
                index + 1,
                entry.User.SteamId64,
                entry.User.PersonaName,
                entry.User.AvatarUrl,
                entry.Value,
                entry.Currency,
                entry.ItemCount,
                entry.User.LastInventoryRefreshAt))
            .ToList();
    }
}
