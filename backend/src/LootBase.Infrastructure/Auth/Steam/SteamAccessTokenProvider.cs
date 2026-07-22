using System.Text.Json;
using LootBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LootBase.Infrastructure.Auth.Steam;

// Mints steamLoginSecure cookies from a long-lived Steam refresh token, and
// asks Steam to rotate that refresh token on every call, persisting whatever
// comes back to the DB. So nobody has to hand-copy a Steam cookie again -
// unless the session gets revoked entirely (password change, etc)
public sealed class SteamAccessTokenProvider(
    HttpClient httpClient,
    LootBaseDbContext dbContext,
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

            var refreshToken = await GetCurrentRefreshTokenAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return null;
            }

            return await RefreshAsync(refreshToken, cancellationToken);
        }
        finally
        {
            RefreshLock.Release();
        }
    }

    private async Task<string?> GetCurrentRefreshTokenAsync(CancellationToken cancellationToken)
    {
        var stored = await dbContext.SteamMarketCredentials
            .Where(credential => credential.Id == SteamMarketCredential.SingletonId)
            .Select(credential => credential.RefreshToken)
            .FirstOrDefaultAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(stored) ? options.MarketRefreshToken : stored;
    }

    private async Task<string?> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var steamId = GetJwtClaim(refreshToken, "sub");
        if (steamId is null)
        {
            logger.LogWarning("Steam market refresh token is not a valid JWT (missing 'sub' claim).");
            return null;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://api.steampowered.com/IAuthenticationService/GenerateAccessTokenForApp/v1/")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["refresh_token"] = refreshToken,
                ["steamid"] = steamId,
                ["renewal_type"] = "1" // k_ETokenRenewalType_Allow - ask Steam to also rotate the refresh token
            }),
            Headers = { Referrer = new Uri("https://steamcommunity.com") }
        };

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Refreshing the Steam market access token failed with status code {StatusCode}. The refresh token may have expired or been revoked.",
                (int)response.StatusCode);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var responseElement = document.RootElement.GetProperty("response");

        var accessToken = responseElement.TryGetProperty("access_token", out var accessTokenElement)
            ? accessTokenElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            logger.LogWarning("Steam access token refresh returned no access_token.");
            return null;
        }

        var rotatedRefreshToken = responseElement.TryGetProperty("refresh_token", out var refreshTokenElement)
            ? refreshTokenElement.GetString()
            : null;
        if (!string.IsNullOrWhiteSpace(rotatedRefreshToken))
        {
            await StoreRefreshTokenAsync(rotatedRefreshToken, cancellationToken);
        }

        var expiresAtUnix = GetJwtClaim(accessToken, "exp");
        cachedExpiresAt = expiresAtUnix is not null && long.TryParse(expiresAtUnix, out var exp)
            ? DateTimeOffset.FromUnixTimeSeconds(exp) - RefreshMargin
            : DateTimeOffset.UtcNow + RefreshMargin;
        cachedCookie = $"steamLoginSecure={steamId}%7C%7C{accessToken}";

        logger.LogInformation("Refreshed the Steam market access token; valid until {ExpiresAt}.", cachedExpiresAt);
        return cachedCookie;
    }

    private async Task StoreRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var existing = await dbContext.SteamMarketCredentials
            .FirstOrDefaultAsync(credential => credential.Id == SteamMarketCredential.SingletonId, cancellationToken);

        if (existing is null)
        {
            dbContext.SteamMarketCredentials.Add(new SteamMarketCredential
            {
                RefreshToken = refreshToken,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            existing.RefreshToken = refreshToken;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
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
