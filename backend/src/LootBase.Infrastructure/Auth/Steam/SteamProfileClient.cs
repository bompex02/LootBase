using System.Text.Json;
using LootBase.Application.Abstractions.Auth;
using Microsoft.Extensions.Options;

namespace LootBase.Infrastructure.Auth.Steam;

public sealed class SteamProfileClient(
    HttpClient httpClient,
    IOptions<SteamOptions> options) : ISteamProfileClient
{
    private readonly SteamOptions options = options.Value;

    public async Task<SteamProfile?> GetProfileAsync(string steamId64, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.WebApiKey))
        {
            return new SteamProfile(steamId64, $"Steam {steamId64[^6..]}", null);
        }

        var requestUri =
            $"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v0002/?key={Uri.EscapeDataString(options.WebApiKey)}&steamids={Uri.EscapeDataString(steamId64)}";

        using var response = await httpClient.GetAsync(requestUri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("response", out var responseElement) ||
            !responseElement.TryGetProperty("players", out var playersElement) ||
            playersElement.GetArrayLength() == 0)
        {
            return null;
        }

        var player = playersElement[0];
        var personaName = player.TryGetProperty("personaname", out var nameElement)
            ? nameElement.GetString() ?? $"Steam {steamId64[^6..]}"
            : $"Steam {steamId64[^6..]}";
        var avatarUrl = player.TryGetProperty("avatarfull", out var avatarElement)
            ? avatarElement.GetString()
            : null;

        return new SteamProfile(steamId64, personaName, avatarUrl);
    }
}
