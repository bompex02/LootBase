using LootBase.Domain.Games;
using LootBase.Domain.Inventory;
using LootBase.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LootBase.Infrastructure.Persistence;

public static class DemoDataSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LootBaseDbContext>();

        await dbContext.Database.EnsureCreatedAsync(cancellationToken);

        if (await dbContext.Users.AnyAsync(cancellationToken))
        {
            return;
        }

        dbContext.Users.AddRange(
            CreateUser("76561198000000001", "Nova", 12890.44m, 62),
            CreateUser("76561198000000002", "Kade", 9814.17m, 48),
            CreateUser("76561198000000003", "Mira", 6320.72m, 37),
            CreateUser("76561198000000004", "Alex", 4212.35m, 29));

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static User CreateUser(string steamId64, string personaName, decimal totalValue, int itemCount)
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-Random.Shared.Next(5, 240));
        var firstPrice = Math.Round(totalValue * 0.58m, 2);
        var secondPrice = Math.Round(totalValue * 0.27m, 2);
        var thirdPrice = Math.Round(totalValue - firstPrice - secondPrice, 2);
        var fillerQuantity = Math.Max(1, itemCount - 2);
        var fillerUnitPrice = Math.Round(thirdPrice / fillerQuantity, 2);

        var user = new User
        {
            SteamId64 = steamId64,
            PersonaName = personaName,
            AvatarUrl = $"https://avatars.cloudflare.steamstatic.com/{steamId64[^8..]}_full.jpg",
            LastInventoryRefreshAt = now
        };

        user.InventoryItems.Add(CreateItem(user, "1", "AK-47 | Redline (Field-Tested)", firstPrice, "Rifle"));
        user.InventoryItems.Add(CreateItem(user, "2", "AWP | Asiimov (Battle-Scarred)", secondPrice, "Sniper Rifle"));
        user.InventoryItems.Add(CreateItem(
            user,
            "3",
            "Assorted CS2 Cases",
            fillerUnitPrice,
            "Container",
            fillerQuantity));
        user.InventorySnapshots.Add(new InventorySnapshot
        {
            AppId = SteamGames.CounterStrike2,
            TotalValue = totalValue,
            CapturedAt = now
        });

        return user;
    }

    private static InventoryItem CreateItem(
        User user,
        string assetSuffix,
        string marketHashName,
        decimal unitPrice,
        string type,
        int quantity = 1)
    {
        return new InventoryItem
        {
            User = user,
            AppId = SteamGames.CounterStrike2,
            AssetId = $"{user.SteamId64}-{assetSuffix}",
            ClassId = $"class-{assetSuffix}",
            MarketHashName = marketHashName,
            DisplayName = marketHashName,
            Type = type,
            Exterior = marketHashName.Contains("Field-Tested", StringComparison.OrdinalIgnoreCase)
                ? "Field-Tested"
                : "Battle-Scarred",
            Rarity = unitPrice > 1000 ? "Covert" : "Classified",
            Quantity = quantity,
            UnitPrice = unitPrice,
            Currency = "EUR",
            LastPricedAt = DateTimeOffset.UtcNow
        };
    }
}
