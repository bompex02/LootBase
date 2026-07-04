namespace LootBase.Application.Abstractions.Auth;

public sealed record SteamLoginValidationResult(
    bool IsValid,
    string? SteamId64,
    string? Error)
{
    public static SteamLoginValidationResult Valid(string steamId64) => new(true, steamId64, null);

    public static SteamLoginValidationResult Invalid(string error) => new(false, null, error);
}
