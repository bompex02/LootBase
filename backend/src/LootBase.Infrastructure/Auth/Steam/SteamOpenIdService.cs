using System.Text.RegularExpressions;
using LootBase.Application.Abstractions.Auth;
using Microsoft.Extensions.Options;

namespace LootBase.Infrastructure.Auth.Steam;

public sealed partial class SteamOpenIdService(
    HttpClient httpClient,
    IOptions<SteamOptions> options) : ISteamOpenIdService
{
    private const string ProviderUrl = "https://steamcommunity.com/openid/login";
    private readonly SteamOptions options = options.Value;

    public Uri BuildLoginUri()
    {
        var query = new Dictionary<string, string>
        {
            ["openid.ns"] = "http://specs.openid.net/auth/2.0",
            ["openid.mode"] = "checkid_setup",
            ["openid.return_to"] = options.ReturnUrl,
            ["openid.realm"] = options.Realm,
            ["openid.identity"] = "http://specs.openid.net/auth/2.0/identifier_select",
            ["openid.claimed_id"] = "http://specs.openid.net/auth/2.0/identifier_select"
        };

        return new Uri($"{ProviderUrl}?{ToQueryString(query)}");
    }

    public async Task<SteamLoginValidationResult> ValidateCallbackAsync(
        IReadOnlyDictionary<string, string> query,
        CancellationToken cancellationToken)
    {
        if (!query.TryGetValue("openid.mode", out var mode) || mode != "id_res")
        {
            return SteamLoginValidationResult.Invalid("Steam did not return an OpenID assertion.");
        }

        if (!query.TryGetValue("openid.claimed_id", out var claimedId))
        {
            return SteamLoginValidationResult.Invalid("Steam response did not include a claimed identity.");
        }

        var steamId64 = ExtractSteamId64(claimedId);
        if (steamId64 is null)
        {
            return SteamLoginValidationResult.Invalid("Steam response did not include a SteamID64.");
        }

        var validationPayload = query
            .Where(item => item.Key.StartsWith("openid.", StringComparison.Ordinal))
            .ToDictionary(item => item.Key, item => item.Value);
        validationPayload["openid.mode"] = "check_authentication";

        using var response = await httpClient.PostAsync(
            ProviderUrl,
            new FormUrlEncodedContent(validationPayload),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return SteamLoginValidationResult.Invalid("Steam OpenID verification request failed.");
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        return responseBody.Contains("is_valid:true", StringComparison.OrdinalIgnoreCase)
            ? SteamLoginValidationResult.Valid(steamId64)
            : SteamLoginValidationResult.Invalid("Steam OpenID assertion was rejected.");
    }

    private static string? ExtractSteamId64(string claimedId)
    {
        var match = SteamIdRegex().Match(claimedId);
        return match.Success ? match.Groups["steamId"].Value : null;
    }

    private static string ToQueryString(IEnumerable<KeyValuePair<string, string>> values)
    {
        return string.Join(
            '&',
            values.Select(item =>
                $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value)}"));
    }

    [GeneratedRegex(@"^https?://steamcommunity\.com/openid/id/(?<steamId>\d{17})$")]
    private static partial Regex SteamIdRegex();
}
