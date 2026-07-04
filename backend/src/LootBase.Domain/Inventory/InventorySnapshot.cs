using LootBase.Domain.Users;

namespace LootBase.Domain.Inventory;

public sealed class InventorySnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public User? User { get; set; }

    public int AppId { get; set; }

    public decimal TotalValue { get; set; }

    public string Currency { get; set; } = "EUR";

    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
}
