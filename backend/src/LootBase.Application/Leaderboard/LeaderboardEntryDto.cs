namespace LootBase.Application.Leaderboard;

public sealed record LeaderboardEntryDto(
    int Rank,
    string SteamId64,
    string PersonaName,
    string? AvatarUrl,
    decimal InventoryValue,
    string Currency,
    int ItemCount,
    DateTimeOffset? LastInventoryRefreshAt);
