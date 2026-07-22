namespace LootBase.Application.Abstractions.Persistence;

public sealed record ItemMetadataDto(
    string MarketHashName,
    string DisplayName,
    string? IconUrl,
    string? Type,
    string? Exterior,
    string? Rarity);
