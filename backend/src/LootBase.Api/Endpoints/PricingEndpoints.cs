using LootBase.Application.Abstractions.Pricing;
using LootBase.Infrastructure.Auth.Steam;
using Microsoft.Extensions.Options;

namespace LootBase.Api.Endpoints;

public static class PricingEndpoints
{
    public static IEndpointRouteBuilder MapPricingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/pricing/items", async (
            string[]? marketHashNames,
            string? currency,
            IPricingCatalog pricingCatalog,
            CancellationToken cancellationToken) =>
        {
            var names = ParseMarketHashNames(marketHashNames);
            if (names.Count == 0)
            {
                return Results.BadRequest(new { error = "Provide at least one marketHashNames value." });
            }

            var items = await pricingCatalog.GetItemsAsync(
                names,
                currency ?? "EUR",
                cancellationToken);

            return Results.Ok(items);
        })
        .WithTags("Pricing");

        app.MapGet("/api/pricing/items/{*marketHashName}", async (
            string marketHashName,
            string? currency,
            IPricingCatalog pricingCatalog,
            CancellationToken cancellationToken) =>
        {
            var item = await pricingCatalog.GetItemAsync(
                marketHashName,
                currency ?? "EUR",
                cancellationToken);

            return item is null ? Results.NotFound() : Results.Ok(item);
        })
        .WithTags("Pricing");

        app.MapGet("/api/pricing/history/{*marketHashName}", async (
            string marketHashName,
            string? currency,
            IPricingHistoryProvider pricingHistory,
            CancellationToken cancellationToken) =>
        {
            var history = await pricingHistory.GetHistoryAsync(
                marketHashName,
                currency ?? "EUR",
                cancellationToken);

            return history is null ? Results.NotFound() : Results.Ok(history);
        })
        .WithTags("Pricing");

        app.MapPost("/api/pricing/backfill/{*marketHashName}", async (
            string marketHashName,
            HttpRequest request,
            IOptions<SteamOptions> steamOptions,
            IPricingHistoryProvider pricingHistory,
            CancellationToken cancellationToken) =>
        {
            var expectedSecret = steamOptions.Value.MarketBackfillSecret;
            if (string.IsNullOrWhiteSpace(expectedSecret) ||
                request.Headers["X-Backfill-Key"] != expectedSecret)
            {
                return Results.Unauthorized();
            }

            var imported = await pricingHistory.BackfillFromSteamAsync(marketHashName, cancellationToken);
            return Results.Ok(new { imported });
        })
        .WithTags("Pricing");

        return app;
    }

    private static IReadOnlyCollection<string> ParseMarketHashNames(IEnumerable<string>? marketHashNames)
    {
        if (marketHashNames is null)
        {
            return [];
        }

        return marketHashNames
            .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }
}
