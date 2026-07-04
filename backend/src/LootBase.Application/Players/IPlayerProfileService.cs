namespace LootBase.Application.Players;

public interface IPlayerProfileService
{
    Task<PlayerProfileDto?> GetAsync(string steamId64, int appId, CancellationToken cancellationToken);
}
