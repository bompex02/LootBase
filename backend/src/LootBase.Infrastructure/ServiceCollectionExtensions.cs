using System.Net;
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
using Npgsql;

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

            options.UseNpgsql(ToNpgsqlConnectionString(connectionString));
        });

        services.Configure<SteamOptions>(options =>
        {
            options.Realm = configuration["Steam:Realm"] ?? options.Realm;
            options.ReturnUrl = configuration["Steam:ReturnUrl"] ?? options.ReturnUrl;
            options.FrontendBaseUrl =
                configuration["Steam:FrontendBaseUrl"] ?? options.FrontendBaseUrl;
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
        services.AddHttpClient<IPricingCatalog, PricingProvider>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.All
            });
        services.AddScoped<IPricingProvider>(sp => sp.GetRequiredService<IPricingCatalog>());

        return services;
    }

    private static string ToNpgsqlConnectionString(string connectionString)
    {
        if (!connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return connectionString;
        }

        var uri = new Uri(connectionString);
        var userInfo = uri.UserInfo.Split(':', 2);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
            SslMode = SslMode.Require
        };

        return builder.ConnectionString;
    }
}
