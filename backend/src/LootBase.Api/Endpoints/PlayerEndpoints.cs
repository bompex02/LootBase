using System.Security.Claims;
using LootBase.Application.Abstractions.Inventory;
using LootBase.Application.Players;
using LootBase.Domain.Games;
using LootBase.Infrastructure.Inventory;

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

            try
            {
                var profile = await refreshService.RefreshAsync(
                    steamId64,
                    SteamGames.CounterStrike2,
                    cancellationToken);

                return Results.Ok(profile);
            }
            catch (InvalidOperationException ex) when (ex.Message == Cs2SteamInventoryProvider.RateLimitMessage)
            {
                return Results.Json(
                    new { code = "steam_rate_limited", error = ex.Message },
                    statusCode: StatusCodes.Status429TooManyRequests);
            }
            catch (InvalidOperationException ex) when (ex.Message == Cs2SteamInventoryProvider.InventoryPrivateMessage)
            {
                return Results.Json(
                    new { code = "steam_inventory_private", error = ex.Message },
                    statusCode: StatusCodes.Status400BadRequest);
            }
            catch (InvalidOperationException ex) when (
                ex.Message.StartsWith(Cs2SteamInventoryProvider.InventoryRequestFailedMessage, StringComparison.Ordinal))
            {
                return Results.Json(
                    new { code = "steam_inventory_error", error = ex.Message },
                    statusCode: StatusCodes.Status502BadGateway);
            }
        })
        .RequireAuthorization()
        .WithTags("Players");

        return app;
    }
}
