namespace LootBase.Application.Abstractions.Pricing;

public sealed record BulkBackfillStatusDto(
    bool IsRunning,
    string? Currency,
    int TotalItems,
    int Processed,
    int AlreadyCovered,
    int Imported,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? LastError)
{
    public static readonly BulkBackfillStatusDto Idle = new(false, null, 0, 0, 0, 0, null, null, null);
}
