using LootBase.Application.Players;

namespace LootBase.Application.Abstractions.Inventory;

public interface IInventoryRefreshService
{
    Task<PlayerProfileDto> RefreshAsync(string steamId64, int appId, CancellationToken cancellationToken);
}
