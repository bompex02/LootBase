using LootBase.Domain.Inventory;
using LootBase.Domain.Pricing;
using LootBase.Domain.Users;
using LootBase.Infrastructure.Auth.Steam;
using Microsoft.EntityFrameworkCore;

namespace LootBase.Infrastructure.Persistence;

public sealed class LootBaseDbContext(DbContextOptions<LootBaseDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();

    public DbSet<InventorySnapshot> InventorySnapshots => Set<InventorySnapshot>();

    public DbSet<ItemPriceSnapshot> ItemPriceSnapshots => Set<ItemPriceSnapshot>();

    public DbSet<SteamMarketCredential> SteamMarketCredentials => Set<SteamMarketCredential>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(user =>
        {
            user.HasKey(x => x.Id);
            user.HasIndex(x => x.SteamId64).IsUnique();
            user.Property(x => x.SteamId64).HasMaxLength(32);
            user.Property(x => x.PersonaName).HasMaxLength(120);
            user.Property(x => x.AvatarUrl).HasMaxLength(512);

            user.HasMany(x => x.InventoryItems)
                .WithOne(x => x.User)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            user.HasMany(x => x.InventorySnapshots)
                .WithOne(x => x.User)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InventoryItem>(item =>
        {
            item.HasKey(x => x.Id);
            item.HasIndex(x => new { x.UserId, x.AppId, x.AssetId }).IsUnique();
            item.Property(x => x.AssetId).HasMaxLength(64);
            item.Property(x => x.ClassId).HasMaxLength(64);
            item.Property(x => x.InstanceId).HasMaxLength(64);
            item.Property(x => x.MarketHashName).HasMaxLength(240);
            item.Property(x => x.DisplayName).HasMaxLength(240);
            item.Property(x => x.IconUrl).HasMaxLength(512);
            item.Property(x => x.Type).HasMaxLength(120);
            item.Property(x => x.Exterior).HasMaxLength(80);
            item.Property(x => x.Rarity).HasMaxLength(80);
            item.Property(x => x.Currency).HasMaxLength(3);
            item.Property(x => x.UnitPrice).HasPrecision(18, 2);
            item.Ignore(x => x.TotalPrice);
        });

        modelBuilder.Entity<InventorySnapshot>(snapshot =>
        {
            snapshot.HasKey(x => x.Id);
            snapshot.HasIndex(x => new { x.UserId, x.AppId, x.CapturedAt });
            snapshot.Property(x => x.Currency).HasMaxLength(3);
            snapshot.Property(x => x.TotalValue).HasPrecision(18, 2);
        });

        modelBuilder.Entity<ItemPriceSnapshot>(snapshot =>
        {
            snapshot.HasKey(x => x.Id);
            snapshot.HasIndex(x => new { x.MarketHashName, x.Currency, x.CapturedDate }).IsUnique();
            snapshot.Property(x => x.MarketHashName).HasMaxLength(240);
            snapshot.Property(x => x.Currency).HasMaxLength(3);
            snapshot.Property(x => x.MinPrice).HasPrecision(18, 2);
            snapshot.Property(x => x.MedianPrice).HasPrecision(18, 2);
            snapshot.Property(x => x.MeanPrice).HasPrecision(18, 2);
            snapshot.Property(x => x.MaxPrice).HasPrecision(18, 2);
            snapshot.Property(x => x.Source).HasMaxLength(16);
        });

        modelBuilder.Entity<SteamMarketCredential>(credential =>
        {
            credential.HasKey(x => x.Id);
            credential.Property(x => x.Id).ValueGeneratedNever();
            credential.Property(x => x.RefreshToken).IsRequired();
        });
    }
}
