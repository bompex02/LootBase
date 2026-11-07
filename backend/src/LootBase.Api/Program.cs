using LootBase.Api;
using LootBase.Api.Endpoints;
using LootBase.Application;
using LootBase.Infrastructure;
using LootBase.Infrastructure.Persistence;
using LootBase.PricePrediction;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddOpenApi();
builder.Services.AddSingleton<PriceTrendModelHolder>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "lootbase.session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.LoginPath = "/api/auth/steam/login";
        options.LogoutPath = "/api/auth/logout";
        options.SlidingExpiration = true;
        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            },
            OnRedirectToAccessDenied = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .GetChildren()
    .Select(origin => origin.Value)
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Cast<string>()
    .ToArray();
if (allowedOrigins.Length == 0)
{
    allowedOrigins = ["http://localhost:3000"];
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<LootBaseDbContext>();
    if (dbContext.Database.IsRelational())
    {
        dbContext.Database.Migrate();

        // Trained and uploaded offline via the LootBase.PricePrediction CLI -
        // no row yet (or no DB at all) just means /api/predictions 503s
        var artifact = await dbContext.PriceTrendModelArtifacts.FindAsync(1);
        if (artifact is not null)
        {
            scope.ServiceProvider.GetRequiredService<PriceTrendModelHolder>().Model =
                PriceTrendModel.LoadFromBytes(artifact.Bytes);
        }
    }
}

// Render (and most PaaS hosts) terminate TLS at the proxy and forward plain
// HTTP internally - without this, the app thinks every request is HTTP.
// Only XForwardedProto is processed (not XForwardedFor): this app never
// reads RemoteIpAddress for anything, so there's no client-IP header to
// spoof by trusting an unlisted proxy.
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto
};
forwardedHeadersOptions.KnownIPNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("GlobalExceptionHandler")
            .LogError(exception, "Unhandled exception while processing {Path}", context.Request.Path);

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new
        {
            code = "internal_error",
            error = "An unexpected error occurred. Please try again later."
        });
    });
});

app.UseCors("frontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthEndpoints();
app.MapAuthEndpoints();
app.MapItemsEndpoints();
app.MapPricingEndpoints();
app.MapLeaderboardEndpoints();
app.MapPlayerEndpoints();
app.MapPredictionEndpoints();

app.Run();
