namespace LootBase.Application.Abstractions.Inventory;

public sealed record InventoryAsset(
    string AssetId,
    string ClassId,
    string? InstanceId,
    string MarketHashName,
    string DisplayName,
    string? IconUrl,
    string? Type,
    string? Exterior,
    string? Rarity,
    int Quantity);
