namespace LootBase.Application.Abstractions.Auth;

public interface ISteamOpenIdService
{
    Uri BuildLoginUri();

    Task<SteamLoginValidationResult> ValidateCallbackAsync(
        IReadOnlyDictionary<string, string> query,
        CancellationToken cancellationToken);
}
