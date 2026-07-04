namespace LootBase.Application.Abstractions.Auth;

public sealed record SteamProfile(
    string SteamId64,
    string PersonaName,
    string? AvatarUrl);
