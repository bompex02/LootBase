using LootBase.Application.Leaderboard;
using LootBase.Application.Players;
using Microsoft.Extensions.DependencyInjection;

namespace LootBase.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ILeaderboardService, LeaderboardService>();
        services.AddScoped<IPlayerProfileService, PlayerProfileService>();

        return services;
    }
}
