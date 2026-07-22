using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LootBase.Infrastructure.Auth.Steam;

// Mints short-lived steamLoginSecure cookies from the long-lived
// Steam:MarketRefreshToken via Steam's (unofficial) GenerateAccessTokenForApp
// endpoint, so nobody has to hand-copy a fresh browser cookie every ~1-2 days.
public sealed class SteamAccessTokenProvider(
    HttpClient httpClient,
    IOptions<SteamOptions> options,
    ILogger<SteamAccessTokenProvider> logger)
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(10);
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);
    private static string? cachedCookie;
    private static DateTimeOffset cachedExpiresAt = DateTimeOffset.MinValue;

    private readonly SteamOptions options = options.Value;

    public async Task<string?> GetMarketCookieAsync(CancellationToken cancellationToken)
    {
        var refreshToken = options.MarketRefreshToken;
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return null;
        }

        if (cachedCookie is not null && DateTimeOffset.UtcNow < cachedExpiresAt)
        {
            return cachedCookie;
        }

        await RefreshLock.WaitAsync(cancellationToken);
        try
        {
            if (cachedCookie is not null && DateTimeOffset.UtcNow < cachedExpiresAt)
            {
                return cachedCookie;
            }

            return await RefreshAsync(refreshToken, cancellationToken);
        }
        finally
        {
            RefreshLock.Release();
        }
    }

    private async Task<string?> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var steamId = GetJwtClaim(refreshToken, "sub");
        if (steamId is null)
        {
            logger.LogWarning("Steam:MarketRefreshToken is not a valid JWT (missing 'sub' claim).");
            return null;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://api.steampowered.com/IAuthenticationService/GenerateAccessTokenForApp/v1/")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["refresh_token"] = refreshToken,
                ["steamid"] = steamId
            }),
            Headers = { Referrer = new Uri("https://steamcommunity.com") }
        };

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Refreshing the Steam market access token failed with status code {StatusCode}. The refresh token may have expired.",
                (int)response.StatusCode);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var accessToken = document.RootElement
            .GetProperty("response")
            .GetProperty("access_token")
            .GetString();

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            logger.LogWarning("Steam access token refresh returned no access_token.");
            return null;
        }

        var expiresAtUnix = GetJwtClaim(accessToken, "exp");
        cachedExpiresAt = expiresAtUnix is not null && long.TryParse(expiresAtUnix, out var exp)
            ? DateTimeOffset.FromUnixTimeSeconds(exp) - RefreshMargin
            : DateTimeOffset.UtcNow + RefreshMargin;
        cachedCookie = $"steamLoginSecure={steamId}%7C%7C{accessToken}";

        logger.LogInformation("Refreshed the Steam market access token; valid until {ExpiresAt}.", cachedExpiresAt);
        return cachedCookie;
    }

    private static string? GetJwtClaim(string jwt, string claim)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(DecodeBase64Url(parts[1]));
            return document.RootElement.TryGetProperty(claim, out var value)
                ? value.ToString()
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            _ => string.Empty
        };
        return Convert.FromBase64String(padded);
    }
}
