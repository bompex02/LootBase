using System.Text.Json;
using LootBase.Infrastructure.Persistence;
using LootBase.PricePrediction;
using Microsoft.Extensions.Caching.Distributed;

namespace LootBase.Api.Endpoints;

public static class PredictionEndpoints
{
    private static readonly DistributedCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
    };

    public static IEndpointRouteBuilder MapPredictionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/predictions/{*marketHashName}", async (
            string marketHashName,
            LootBaseDbContext db,
            IDistributedCache cache,
            PriceTrendModelHolder modelHolder,
            CancellationToken cancellationToken) =>
        {
            if (modelHolder.Model is not { } model)
            {
                return Results.Problem("No trained price-trend model available.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var cacheKey = $"prediction:{marketHashName}";
            var cached = await cache.GetStringAsync(cacheKey, cancellationToken);
            if (cached is not null)
            {
                return Results.Content(cached, "application/json");
            }

            var features = await PriceTrendDataset.GetLatestFeaturesAsync(db, marketHashName, cancellationToken);
            if (features is null)
            {
                return Results.NotFound();
            }

            var prediction = model.Predict(features);
            var json = JsonSerializer.Serialize(new { goesUp = prediction.GoesUp, probability = prediction.Probability });

            // TTL-only cache, no date in the key: the entry simply falls out
            // ~24h after it was written, so the next request (same day or
            // next) recomputes - no cleanup job needed
            await cache.SetStringAsync(cacheKey, json, CacheOptions, cancellationToken);

            return Results.Content(json, "application/json");
        })
        .WithTags("Predictions");

        return app;
    }
}
