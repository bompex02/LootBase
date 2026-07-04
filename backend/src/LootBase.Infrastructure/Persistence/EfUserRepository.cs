using LootBase.Application.Abstractions.Persistence;
using LootBase.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace LootBase.Infrastructure.Persistence;

public sealed class EfUserRepository(LootBaseDbContext dbContext) : IUserRepository
{
    public Task<User?> GetBySteamIdAsync(string steamId64, CancellationToken cancellationToken)
    {
        return dbContext.Users
            .Include(user => user.InventoryItems)
            .Include(user => user.InventorySnapshots)
            .FirstOrDefaultAsync(user => user.SteamId64 == steamId64, cancellationToken);
    }

    public async Task<User> UpsertSteamUserAsync(
        string steamId64,
        string personaName,
        string? avatarUrl,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users
            .Include(x => x.InventoryItems)
            .FirstOrDefaultAsync(x => x.SteamId64 == steamId64, cancellationToken);

        if (user is null)
        {
            user = new User
            {
                SteamId64 = steamId64,
                PersonaName = personaName,
                AvatarUrl = avatarUrl
            };

            dbContext.Users.Add(user);
        }
        else
        {
            user.PersonaName = personaName;
            user.AvatarUrl = avatarUrl;
            user.LastSeenAt = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return user;
    }

    public async Task<IReadOnlyCollection<User>> GetUsersWithInventoriesAsync(
        int appId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Users
            .Include(user => user.InventoryItems.Where(item => item.AppId == appId))
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
