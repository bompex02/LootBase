using LootBase.Domain.Inventory;

namespace LootBase.Domain.Users;

public sealed class User
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string SteamId64 { get; set; }

    public required string PersonaName { get; set; }

    public string? AvatarUrl { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastInventoryRefreshAt { get; set; }

    public ICollection<InventoryItem> InventoryItems { get; set; } = [];

    public ICollection<InventorySnapshot> InventorySnapshots { get; set; } = [];
}
