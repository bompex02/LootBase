using Microsoft.ML.Data;

namespace LootBase.PricePrediction;

/// <summary>ML.NET's output for one <see cref="TrainingSample"/> prediction.</summary>
public sealed class TrendPrediction
{
    [ColumnName("PredictedLabel")]

    // goes up or down
    public bool GoesUp { get; init; }

    // probability of going up in percentage (0.0 to 1.0)
    public float Probability { get; init; }
}
