namespace LootBase.Infrastructure.Auth.Steam;

public sealed class SteamOptions
{
    public string Realm { get; set; } = "http://localhost:5188/";

    public string ReturnUrl { get; set; } = "http://localhost:5188/api/auth/steam/callback";

    public string FrontendBaseUrl { get; set; } = "http://localhost:3000";

    public string? WebApiKey { get; set; }

    // "steamLoginSecure=...; sessionid=..." from a logged-in steamcommunity.com
    // browser session - required to call the (unofficial) market price history
    // endpoint, which Steam only exposes to authenticated sessions
    public string? MarketSessionCookie { get; set; }

    // Shared secret required to trigger a price-history backfill. There's no
    // admin/role system in this app, so this is the simplest gate that keeps
    // the endpoint from being hammered by anyone who finds it
    public string? MarketBackfillSecret { get; set; }
}
