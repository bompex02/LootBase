using Microsoft.ML;
using Microsoft.ML.Data;

namespace LootBase.PricePrediction;

/// <summary>Trains, evaluates, (de)serializes, and runs single predictions for the "does the price go up within the horizon" binary classifier.</summary>
public sealed class PriceTrendModel
{
    public const string DefaultModelPath = "price-trend-model.zip";

    // seed: 1 is a good balance between reproducibility and randomness, 
    // and is the default for ML.NET  
    private readonly MLContext ml = new(seed: 1);
    private ITransformer? trained;
    private DataViewSchema? trainingSchema;

    public void Train(IReadOnlyList<TrainingSample> trainSamples)
    {
        // ML.NET pipeline: concatenate features into one vector, then train a FastTree binary classifier
        var pipeline = ml.Transforms
            .Concatenate("Features", nameof(TrainingSample.Price), nameof(TrainingSample.Return1), nameof(TrainingSample.Return3),
                nameof(TrainingSample.Quantity), nameof(TrainingSample.Volatility), nameof(TrainingSample.QuantityTrend))
            .Append(ml.BinaryClassification.Trainers.FastTree(labelColumnName: nameof(TrainingSample.GoesUp)));

        // load training data from in-memory collection
        var trainingData = ml.Data.LoadFromEnumerable(trainSamples);
        
        // train the model
        trained = pipeline.Fit(trainingData);
        trainingSchema = trainingData.Schema;
    }

    public void Save(string path = DefaultModelPath)
    {
        if (trained is null || trainingSchema is null)
        {
            throw new InvalidOperationException("Call Train before Save.");
        }

        ml.Model.Save(trained, trainingSchema, path);
        Console.WriteLine($"Saved model to {path}");
    }

    public static PriceTrendModel Load(string path = DefaultModelPath)
    {
        var model = new PriceTrendModel();
        model.trained = model.ml.Model.Load(path, out _);
        return model;
    }

    /// <summary>Predicts whether the price will rise, using today's (or the latest known) features.</summary>
    public TrendPrediction Predict(TrainingSample todayFeatures)
    {
        if (trained is null)
        {
            throw new InvalidOperationException("Call Train or Load before Predict.");
        }

        using var engine = ml.Model.CreatePredictionEngine<TrainingSample, TrendPrediction>(trained);
        return engine.Predict(todayFeatures);
    }

    public BinaryClassificationMetrics Evaluate(IReadOnlyList<TrainingSample> testSamples)
    {
        if (trained is null)
        {
            throw new InvalidOperationException("Call Train before Evaluate.");
        }

        return ml.BinaryClassification.Evaluate(
            trained.Transform(ml.Data.LoadFromEnumerable(testSamples)),
            labelColumnName: nameof(TrainingSample.GoesUp));
    }

    public void PrintEvaluation(IReadOnlyList<TrainingSample> testSamples)
    {
        var metrics = Evaluate(testSamples);

        Console.WriteLine($"Accuracy: {metrics.Accuracy:P2}");
        Console.WriteLine($"AUC:      {metrics.AreaUnderRocCurve:F3}");
        Console.WriteLine($"F1:       {metrics.F1Score:F3}");

        // Baseline to beat: always predicting the majority class
        var upShare = testSamples.Count(s => s.GoesUp) / (double)testSamples.Count;
        Console.WriteLine($"Always-guess-majority baseline: {Math.Max(upShare, 1 - upShare):P2}");
    }
}
