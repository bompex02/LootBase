namespace LootBase.Infrastructure.Auth.Steam;

public sealed class SteamOptions
{
    public string Realm { get; set; } = "http://localhost:5188/";

    public string ReturnUrl { get; set; } = "http://localhost:5188/api/auth/steam/callback";

    public string FrontendBaseUrl { get; set; } = "http://localhost:3000";

    public string? WebApiKey { get; set; }

    // Steam's long-lived refresh token. SteamAccessTokenProvider mints short-lived
    // access tokens from it, so this only needs replacing every ~1 year, not daily
    public string? MarketRefreshToken { get; set; }

    // Gate for the backfill endpoint - no admin/role system here, so a shared secret is the simplest guard
    public string? MarketBackfillSecret { get; set; }
}
