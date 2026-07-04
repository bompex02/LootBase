namespace LootBase.Application.Abstractions.Auth;

public interface ISteamProfileClient
{
    Task<SteamProfile?> GetProfileAsync(string steamId64, CancellationToken cancellationToken);
}
