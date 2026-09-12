namespace LootBase.Application.Abstractions.Persistence;

public interface IItemCatalogRepository
{
    // Display metadata (icon, type, exterior, rarity) is intrinsic to the skin,
    // not the owner - any inventory row with this market hash name has it.
    Task<ItemMetadataDto?> GetMetadataAsync(string marketHashName, CancellationToken cancellationToken);
}
