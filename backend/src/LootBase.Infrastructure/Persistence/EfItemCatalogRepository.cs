using LootBase.Application.Abstractions.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LootBase.Infrastructure.Persistence;

public sealed class EfItemCatalogRepository(LootBaseDbContext dbContext) : IItemCatalogRepository
{
    public async Task<ItemMetadataDto?> GetMetadataAsync(string marketHashName, CancellationToken cancellationToken)
    {
        var item = await dbContext.InventoryItems
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.MarketHashName == marketHashName, cancellationToken);

        return item is null
            ? null
            : new ItemMetadataDto(item.MarketHashName, item.DisplayName, item.IconUrl, item.Type, item.Exterior, item.Rarity);
    }
}
