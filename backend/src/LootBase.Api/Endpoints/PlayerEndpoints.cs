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

        app.MapGet("/api/me", async (
            ClaimsPrincipal principal,
            IPlayerProfileService playerProfiles,
            CancellationToken cancellationToken) =>
        {
            var steamId64 = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (steamId64 is null)
            {
                return Results.Unauthorized();
            }

            var profile = await playerProfiles.GetAsync(
                steamId64,
                SteamGames.CounterStrike2,
                cancellationToken);

            return profile is null ? Results.NotFound() : Results.Ok(profile);
        })
        .RequireAuthorization()
        .WithTags("Players");

        app.MapPost("/api/me/inventory/refresh", async (
            ClaimsPrincipal principal,
            IInventoryRefreshService refreshService,
            CancellationToken cancellationToken) =>
        {
            var steamId64 = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (steamId64 is null)
            {
                return Results.Unauthorized();
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
