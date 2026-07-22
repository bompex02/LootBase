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
            if (!IsAuthorizedForBackfill(request, steamOptions.Value))
            {
                return Results.Unauthorized();
            }

            var imported = await pricingHistory.BackfillFromSteamAsync(marketHashName, cancellationToken);
            return Results.Ok(new { imported = imported ?? 0, success = imported is not null });
        })
        .WithTags("Pricing");

        app.MapPost("/api/pricing/backfill-all", (
            HttpRequest request,
            string? currency,
            IOptions<SteamOptions> steamOptions,
            IPricingHistoryProvider pricingHistory,
            IServiceScopeFactory scopeFactory,
            ILoggerFactory loggerFactory) =>
        {
            if (!IsAuthorizedForBackfill(request, steamOptions.Value))
            {
                return Results.Unauthorized();
            }

            if (!pricingHistory.TryStartBulkBackfill())
            {
                return Results.Conflict(new
                {
                    started = false,
                    note = "A bulk backfill is already running; not starting a second one.",
                    status = pricingHistory.GetBulkBackfillStatus()
                });
            }

            var effectiveCurrency = currency ?? "EUR";
            var logger = loggerFactory.CreateLogger("PricingBulkBackfill");

            _ = Task.Run(async () =>
            {
                using var scope = scopeFactory.CreateScope();
                try
                {
                    var historyProvider = scope.ServiceProvider.GetRequiredService<IPricingHistoryProvider>();
                    await historyProvider.BackfillAllFromSteamAsync(effectiveCurrency, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Bulk Steam backfill crashed.");
                }
            });

            return Results.Accepted(value: new
            {
                started = true,
                note = "Runs in the background over every Skinport-known item; watch the API logs for progress."
            });
        })
        .WithTags("Pricing");

        app.MapGet("/api/pricing/backfill-all/status", (
            HttpRequest request,
            IOptions<SteamOptions> steamOptions,
            IPricingHistoryProvider pricingHistory) =>
        {
            if (!IsAuthorizedForBackfill(request, steamOptions.Value))
            {
                return Results.Unauthorized();
            }

            return Results.Ok(pricingHistory.GetBulkBackfillStatus());
        })
        .WithTags("Pricing");

        return app;
    }

    // checks the X-Backfill-Key header against the configured secret; if the secret is not set, no requests are authorized
    private static bool IsAuthorizedForBackfill(HttpRequest request, SteamOptions steamOptions)
    {
        var expectedSecret = steamOptions.MarketBackfillSecret;
        return !string.IsNullOrWhiteSpace(expectedSecret) &&
            request.Headers["X-Backfill-Key"] == expectedSecret;
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
