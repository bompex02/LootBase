using System.Security.Claims;
using LootBase.Application.Abstractions.Inventory;
using LootBase.Application.Players;
using LootBase.Domain.Games;

namespace LootBase.Api.Endpoints;

public static class PlayerEndpoints
{
    public static IEndpointRouteBuilder MapPlayerEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/players/{steamId64}", async (
            string steamId64,
            IPlayerProfileService playerProfiles,
            int? appId,
            CancellationToken cancellationToken) =>
        {
            var profile = await playerProfiles.GetAsync(
                steamId64,
                appId ?? SteamGames.CounterStrike2,
                cancellationToken);

            return profile is null ? Results.NotFound() : Results.Ok(profile);
        })
        .WithTags("Players");

        app.MapPost("/api/players/{steamId64}/inventory/refresh", async (
            string steamId64,
            ClaimsPrincipal principal,
            IInventoryRefreshService refreshService,
            CancellationToken cancellationToken) =>
        {
            var callerSteamId64 = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (callerSteamId64 != steamId64)
            {
                return Results.Forbid();
            }

            var profile = await refreshService.RefreshAsync(
                steamId64,
                SteamGames.CounterStrike2,
                cancellationToken);

            return Results.Ok(profile);
        })
        .RequireAuthorization()
        .WithTags("Players");

        return app;
    }
}
