using LootBase.Domain.Users;

namespace LootBase.Domain.Inventory;

public sealed class InventoryItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public User? User { get; set; }

    public int AppId { get; set; }

    public required string AssetId { get; set; }

    public required string ClassId { get; set; }

    public string? InstanceId { get; set; }

    public required string MarketHashName { get; set; }

    public required string DisplayName { get; set; }

    public string? IconUrl { get; set; }

    public string? Type { get; set; }

    public string? Exterior { get; set; }

    public string? Rarity { get; set; }

    public int Quantity { get; set; } = 1;

    public decimal UnitPrice { get; set; }

    public string Currency { get; set; } = "EUR";

    public DateTimeOffset? LastPricedAt { get; set; }

    public decimal TotalPrice => UnitPrice * Quantity;
}
