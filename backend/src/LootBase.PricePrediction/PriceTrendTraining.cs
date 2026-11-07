using LootBase.Domain.Pricing;
using LootBase.Infrastructure.Persistence;
using Microsoft.ML.Data;

namespace LootBase.PricePrediction;

// Shared by the CLI and the Api's /api/predictions/retrain endpoint so both
// train/upload the exact same way and never drift out of sync
public static class PriceTrendTraining
{
    public const int HorizonDays = 7;
    public const int TestWindowDays = 10;

    public sealed record Result(
        PriceTrendModel Model,
        IReadOnlyList<TrainingSample> Train,
        IReadOnlyList<TrainingSample> Test,
        BinaryClassificationMetrics Metrics);

    public static async Task<Result> RunAsync(LootBaseDbContext db, CancellationToken cancellationToken = default)
    {
        var dataset = await PriceTrendDataset.LoadAsync(db, HorizonDays, cancellationToken);
        var (train, test) = dataset.SplitByDate(TestWindowDays);

        var model = new PriceTrendModel();
        model.Train(train);
        var metrics = model.Evaluate(test);

        var bytes = model.SaveToBytes();
        var artifact = await db.PriceTrendModelArtifacts.FindAsync([1], cancellationToken);
        if (artifact is null)
        {
            db.PriceTrendModelArtifacts.Add(new PriceTrendModelArtifact { Bytes = bytes, TrainedAt = DateTime.UtcNow });
        }
        else
        {
            artifact.Bytes = bytes;
            artifact.TrainedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(cancellationToken);

        return new Result(model, train, test, metrics);
    }
}
