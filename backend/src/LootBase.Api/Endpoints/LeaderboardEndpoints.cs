using LootBase.Application.Leaderboard;
using LootBase.Domain.Games;

namespace LootBase.Api.Endpoints;

public static class LeaderboardEndpoints
{
    public static IEndpointRouteBuilder MapLeaderboardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/leaderboard", async (
            ILeaderboardService leaderboard,
            int? appId,
            int? limit,
            CancellationToken cancellationToken) =>
        {
            var entries = await leaderboard.GetAsync(
                appId ?? SteamGames.CounterStrike2,
                Math.Clamp(limit ?? 50, 1, 100),
                cancellationToken);

            return Results.Ok(entries);
        })
        .WithTags("Leaderboard");

        return app;
    }
}
