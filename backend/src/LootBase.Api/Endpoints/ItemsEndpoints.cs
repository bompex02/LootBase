using LootBase.Application.Abstractions.Persistence;

namespace LootBase.Api.Endpoints;

public static class ItemsEndpoints
{
    public static IEndpointRouteBuilder MapItemsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/items/{*marketHashName}", async (
            string marketHashName,
            IItemCatalogRepository itemCatalog,
            CancellationToken cancellationToken) =>
        {
            var metadata = await itemCatalog.GetMetadataAsync(marketHashName, cancellationToken);
            return metadata is null ? Results.NotFound() : Results.Ok(metadata);
        })
        .WithTags("Items");

        return app;
    }
}
