using LootBase.Domain.Users;

namespace LootBase.Application.Abstractions.Persistence;

public interface IUserRepository
{
    Task<User?> GetBySteamIdAsync(string steamId64, CancellationToken cancellationToken);

    Task<User> UpsertSteamUserAsync(string steamId64, string personaName, string? avatarUrl, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<User>> GetUsersWithInventoriesAsync(int appId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
