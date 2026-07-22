namespace LootBase.Infrastructure.Auth.Steam;

// Single-row table holding the current Steam market refresh token.
// Steam:MarketRefreshToken (config) only seeds the very first row - after
// that, SteamAccessTokenProvider keeps this updated with whatever rotated
// token Steam hands back, so the token never has to be copied by hand again.
public sealed class SteamMarketCredential
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public string RefreshToken { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; }
}
