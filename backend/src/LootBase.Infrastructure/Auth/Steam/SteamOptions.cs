namespace LootBase.Infrastructure.Auth.Steam;

public sealed class SteamOptions
{
    public string Realm { get; set; } = "http://localhost:5188/";

    public string ReturnUrl { get; set; } = "http://localhost:5188/api/auth/steam/callback";

    public string FrontendBaseUrl { get; set; } = "http://localhost:3000";

    public string? WebApiKey { get; set; }

    // Long-lived refresh token (the "steamRefreshToken" cookie's JWT) from a
    // logged-in steamcommunity.com session. SteamAccessTokenProvider exchanges
    // it for short-lived access tokens on demand, so this only needs to be
    // replaced when the refresh token itself expires (~1 year), unlike a raw
    // steamLoginSecure cookie which expires roughly daily.
    public string? MarketRefreshToken { get; set; }

    // Shared secret required to trigger a price-history backfill. There's no
    // admin/role system in this app, so this is the simplest gate that keeps
    // the endpoint from being hammered by anyone who finds it
    public string? MarketBackfillSecret { get; set; }
}
