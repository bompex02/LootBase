namespace LootBase.Domain.Pricing;

// Single row (Id is always 1) - there's only ever one "current" trained
// model, trained offline and uploaded here so the Api can load it without
// needing the model file baked into its Docker image
public sealed class PriceTrendModelArtifact
{
    public int Id { get; set; } = 1;

    public byte[] Bytes { get; set; } = [];

    public DateTime TrainedAt { get; set; }
}
