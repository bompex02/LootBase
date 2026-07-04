namespace LootBase.Application.Players;

public sealed record PlayerProfileDto(
    string SteamId64,
    string PersonaName,
    string? AvatarUrl,
    decimal InventoryValue,
    string Currency,
    int ItemCount,
    DateTimeOffset? LastInventoryRefreshAt,
    IReadOnlyList<InventoryItemDto> TopItems);
