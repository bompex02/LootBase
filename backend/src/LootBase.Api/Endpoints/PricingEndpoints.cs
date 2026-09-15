using System.Security.Cryptography;
using System.Text;
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

        // Public and unauthenticated on purpose: read-only counts, no Steam
        // calls, nothing sensitive - safe for a monitoring page to poll
        app.MapGet("/api/pricing/status", async (
            string? currency,
            IPricingHistoryProvider pricingHistory,
            CancellationToken cancellationToken) =>
        {
            var status = await pricingHistory.GetPipelineStatusAsync(currency ?? "EUR", cancellationToken);
            return Results.Ok(status);
        })
        .WithTags("Pricing");

        app.MapPost("/api/pricing/backfill/{*marketHashName}", async (
            string marketHashName,
            HttpRequest request,
            IOptions<SteamOptions> steamOptions,
            IPricingHistoryProvider pricingHistory,
            CancellationToken cancellationToken) =>
        {
            if (!IsAuthorizedForPricingOps(request, steamOptions.Value))
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
            if (!IsAuthorizedForPricingOps(request, steamOptions.Value))
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

        // Meant for an external daily scheduler (e.g. a cron job hitting this
        // URL) - unlike backfill-all this runs to completion within the
        // request, no fire-and-forget background task involved.
        app.MapPost("/api/pricing/snapshot-all", async (
            HttpRequest request,
            string? currency,
            IOptions<SteamOptions> steamOptions,
            IPricingHistoryProvider pricingHistory,
            CancellationToken cancellationToken) =>
        {
            if (!IsAuthorizedForPricingOps(request, steamOptions.Value))
            {
                return Results.Unauthorized();
            }

            var written = await pricingHistory.SnapshotAllAsync(currency ?? "EUR", cancellationToken);
            return Results.Ok(new { written });
        })
        .WithTags("Pricing");

        // Meant for a scheduler that pings every few minutes (e.g. a GitHub
        // Actions cron) rather than one long-lived background task - each
        // call finishes within the request, so it survives a host that
        // sleeps between requests (unlike backfill-all's Task.Run).
        app.MapPost("/api/pricing/backfill-batch", async (
            HttpRequest request,
            string? currency,
            int? batchSize,
            IOptions<SteamOptions> steamOptions,
            IPricingHistoryProvider pricingHistory,
            CancellationToken cancellationToken) =>
        {
            if (!IsAuthorizedForPricingOps(request, steamOptions.Value))
            {
                return Results.Unauthorized();
            }

            var result = await pricingHistory.BackfillNextBatchFromSteamAsync(
                currency ?? "EUR", batchSize ?? 20, cancellationToken);
            return Results.Ok(result);
        })
        .WithTags("Pricing");

        app.MapGet("/api/pricing/backfill-all/status", (
            HttpRequest request,
            IOptions<SteamOptions> steamOptions,
            IPricingHistoryProvider pricingHistory) =>
        {
            if (!IsAuthorizedForPricingOps(request, steamOptions.Value))
            {
                return Results.Unauthorized();
            }

            return Results.Ok(pricingHistory.GetBulkBackfillStatus());
        })
        .WithTags("Pricing");

        return app;
    }

    // Gates every internal pricing job (backfill + snapshot-all), not just
    // backfill - checks the X-Backfill-Key header against the configured
    // secret; if the secret is not set, no requests are authorized
    private static bool IsAuthorizedForPricingOps(HttpRequest request, SteamOptions steamOptions)
    {
        var expectedSecret = steamOptions.MarketBackfillSecret;
        var providedSecret = request.Headers["X-Backfill-Key"].ToString();
        return !string.IsNullOrWhiteSpace(expectedSecret) &&
            CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(providedSecret),
                Encoding.UTF8.GetBytes(expectedSecret));
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
