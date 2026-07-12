using LootBase.Application.Abstractions.Pricing;

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
