namespace LootBase.Application.Leaderboard;

public interface ILeaderboardService
{
    Task<IReadOnlyList<LeaderboardEntryDto>> GetAsync(int appId, int limit, CancellationToken cancellationToken);
}
