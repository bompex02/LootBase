namespace LootBase.Application.Players;

public sealed record InventoryItemDto(
    string AssetId,
    string MarketHashName,
    string DisplayName,
    string? IconUrl,
    string? Type,
    string? Exterior,
    string? Rarity,
    int Quantity,
    decimal UnitPrice,
    decimal TotalPrice,
    string Currency);
