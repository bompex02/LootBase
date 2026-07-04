namespace LootBase.Infrastructure.Auth.Steam;

public sealed class SteamOptions
{
    public string Realm { get; set; } = "http://localhost:5188/";

    public string ReturnUrl { get; set; } = "http://localhost:5188/api/auth/steam/callback";

    public string FrontendAuthSuccessUrl { get; set; } = "http://localhost:3000/me";

    public string? WebApiKey { get; set; }
}
