namespace LootBase.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health", () => Results.Ok(new
        {
            status = "ok",
            service = "LootBase.Api",
            framework = ".NET 10"
        }))
        .WithTags("Health");

        return app;
    }
}
