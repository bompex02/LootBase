namespace LootBase.Infrastructure.Auth.Steam;

// Single-row table for the current Steam refresh token. Config only seeds the
// first row; SteamAccessTokenProvider keeps it updated after that
public sealed class SteamMarketCredential
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public string RefreshToken { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; }
}
