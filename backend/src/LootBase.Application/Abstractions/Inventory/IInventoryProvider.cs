namespace LootBase.Application.Abstractions.Inventory;

public interface IInventoryProvider
{
    int AppId { get; }

    Task<IReadOnlyCollection<InventoryAsset>> GetInventoryAsync(string steamId64, CancellationToken cancellationToken);
}
