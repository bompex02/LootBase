using LootBase.Application.Abstractions.Auth;
using LootBase.Application.Abstractions.Inventory;
using LootBase.Application.Abstractions.Persistence;
using LootBase.Application.Abstractions.Pricing;
using LootBase.Infrastructure.Auth.Steam;
using LootBase.Infrastructure.Inventory;
using LootBase.Infrastructure.Persistence;
using LootBase.Infrastructure.Pricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LootBase.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("LootBase");
        var redisConnectionString =
            configuration.GetConnectionString("Redis") ??
            configuration["Redis:ConnectionString"];

        services.AddDbContext<LootBaseDbContext>(options =>
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                options.UseInMemoryDatabase("lootbase-dev");
                return;
            }

            options.UseNpgsql(connectionString);
        });

        services.Configure<SteamOptions>(options =>
        {
            options.Realm = configuration["Steam:Realm"] ?? options.Realm;
            options.ReturnUrl = configuration["Steam:ReturnUrl"] ?? options.ReturnUrl;
            options.FrontendAuthSuccessUrl =
                configuration["Steam:FrontendAuthSuccessUrl"] ?? options.FrontendAuthSuccessUrl;
            options.WebApiKey = configuration["Steam:WebApiKey"];
        });

        if (string.IsNullOrWhiteSpace(redisConnectionString))
        {
            services.AddDistributedMemoryCache();
        }
        else
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnectionString;
                options.InstanceName = "lootbase:";
            });
        }

        services.AddScoped<IUserRepository, EfUserRepository>();
        services.AddScoped<IInventoryRefreshService, InventoryRefreshService>();
        services.AddHttpClient<ISteamOpenIdService, SteamOpenIdService>();
        services.AddHttpClient<ISteamProfileClient, SteamProfileClient>();
        services.AddHttpClient<IInventoryProvider, Cs2SteamInventoryProvider>();
        services.AddHttpClient<IPricingProvider, SteamMarketPricingProvider>();

        return services;
    }
}
