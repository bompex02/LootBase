using System.Net;
using LootBase.Application.Abstractions.Auth;
using LootBase.Application.Abstractions.Inventory;
using LootBase.Application.Abstractions.Persistence;
using LootBase.Application.Abstractions.Pricing;
using LootBase.Infrastructure.Auth.Steam;
using LootBase.Infrastructure.Inventory;
using LootBase.Infrastructure.Persistence;
using LootBase.Infrastructure.Pricing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

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
            options.FrontendBaseUrl =
                configuration["Steam:FrontendBaseUrl"] ?? options.FrontendBaseUrl;
            options.WebApiKey = configuration["Steam:WebApiKey"];
            options.MarketRefreshToken = configuration["Steam:MarketRefreshToken"];
            options.MarketBackfillSecret = configuration["Steam:MarketBackfillSecret"];
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

            // Keep cookie encryption keys in Redis, not local disk, so logins survive a redeploy
            var redisConnection = ConnectionMultiplexer.Connect(redisConnectionString);
            services.AddSingleton<IConnectionMultiplexer>(redisConnection);
            services.AddDataProtection()
                .SetApplicationName("LootBase")
                .PersistKeysToStackExchangeRedis(redisConnection, "LootBase-DataProtection-Keys");
        }

        services.AddScoped<IUserRepository, EfUserRepository>();
        services.AddScoped<IItemCatalogRepository, EfItemCatalogRepository>();
        services.AddScoped<IInventoryRefreshService, InventoryRefreshService>();
        services.AddHttpClient<ISteamOpenIdService, SteamOpenIdService>();
        services.AddHttpClient<ISteamProfileClient, SteamProfileClient>();
        services.AddHttpClient<IInventoryProvider, Cs2SteamInventoryProvider>();
        services.AddHttpClient<SteamAccessTokenProvider>();
        services.AddHttpClient<ISteamMarketHistoryClient, SteamMarketHistoryClient>();
        services.AddScoped<ItemPriceSnapshotStore>();
        services.AddHttpClient<IPricingCatalog, PricingProvider>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.All
            });
        services.AddScoped<IPricingProvider>(sp => sp.GetRequiredService<IPricingCatalog>());
        services.AddScoped<IPricingHistoryProvider>(sp => (PricingProvider)sp.GetRequiredService<IPricingCatalog>());

        return services;
    }
}
