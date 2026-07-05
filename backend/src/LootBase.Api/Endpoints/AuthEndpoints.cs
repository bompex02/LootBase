using System.Security.Claims;
using LootBase.Application.Abstractions.Auth;
using LootBase.Application.Abstractions.Persistence;
using LootBase.Domain.Games;
using LootBase.Infrastructure.Auth.Steam;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace LootBase.Api.Endpoints;

public static class AuthEndpoints
{
    public const string SteamIdCookieName = "lootbase.steamId64";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapGet("/steam/login", (ISteamOpenIdService steamOpenId) =>
            Results.Redirect(steamOpenId.BuildLoginUri().ToString()));

        group.MapGet("/steam/callback", async (
            HttpContext httpContext,
            ISteamOpenIdService steamOpenId,
            ISteamProfileClient steamProfiles,
            IUserRepository users,
            IOptions<SteamOptions> options,
            CancellationToken cancellationToken) =>
        {
            var query = httpContext.Request.Query.ToDictionary(
                item => item.Key,
                item => item.Value.ToString());
            var validation = await steamOpenId.ValidateCallbackAsync(query, cancellationToken);

            if (!validation.IsValid || validation.SteamId64 is null)
            {
                return Results.BadRequest(new { error = validation.Error ?? "Invalid Steam login." });
            }

            var profile = await steamProfiles.GetProfileAsync(validation.SteamId64, cancellationToken)
                ?? new SteamProfile(validation.SteamId64, $"Steam {validation.SteamId64[^6..]}", null);
            var user = await users.UpsertSteamUserAsync(
                profile.SteamId64,
                profile.PersonaName,
                profile.AvatarUrl,
                cancellationToken);

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.SteamId64),
                new(ClaimTypes.Name, user.PersonaName),
                new("steam_id", user.SteamId64),
                new("default_appid", SteamGames.CounterStrike2.ToString())
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await httpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity));

            httpContext.Response.Cookies.Append(SteamIdCookieName, user.SteamId64, new CookieOptions
            {
                HttpOnly = false,
                SameSite = SameSiteMode.Lax
            });

            return Results.Redirect($"{options.Value.FrontendBaseUrl}/players/{user.SteamId64}");
        });

        group.MapPost("/logout", async (HttpContext httpContext) =>
        {
            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            httpContext.Response.Cookies.Delete(SteamIdCookieName);
            return Results.NoContent();
        });

        return app;
    }
}
