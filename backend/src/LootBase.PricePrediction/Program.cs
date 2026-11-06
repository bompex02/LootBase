using LootBase.Infrastructure.Persistence;
using LootBase.PricePrediction;
using Microsoft.EntityFrameworkCore;

const int HorizonDays = 7;
const int TestWindowDays = 10;
const int CrossValidationFolds = 4;

PriceTrendDataset.SelfCheck();

// Load the Postgres connection string from the environment variable (currently neon database), and connect to the database
var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__LootBase")
    ?? throw new InvalidOperationException("Set ConnectionStrings__LootBase to the Postgres connection string.");

var dbOptions = new DbContextOptionsBuilder<LootBaseDbContext>().UseNpgsql(connectionString).Options;
await using var db = new LootBaseDbContext(dbOptions);

if (args is ["predict", var marketHashName])
{
    var features = await PriceTrendDataset.GetLatestFeaturesAsync(db, marketHashName);
    if (features is null)
    {
        Console.WriteLine($"Not enough price history for '{marketHashName}' yet.");
        return;
    }

    var prediction = PriceTrendModel.Load().Predict(features);
    Console.WriteLine($"{marketHashName}: {(prediction.GoesUp ? "UP" : "DOWN")} (probability {prediction.Probability:P1})");
    return;
}

var dataset = await PriceTrendDataset.LoadAsync(db, HorizonDays);

// Cross-validate first: is the single-split number from before trustworthy,
// or was it a lucky/unlucky slice of the timeline?
Console.WriteLine($"\n--- {CrossValidationFolds}-fold rolling time-series cross-validation ---");
var accuracies = new List<double>();
var aucs = new List<double>();
var fold = 1;

foreach (var (foldTrain, foldTest) in dataset.RollingSplits(TestWindowDays, CrossValidationFolds))
{
    var foldModel = new PriceTrendModel();
    foldModel.Train(foldTrain);
    var metrics = foldModel.Evaluate(foldTest);

    accuracies.Add(metrics.Accuracy);
    aucs.Add(metrics.AreaUnderRocCurve);
    Console.WriteLine($"Fold {fold}: Accuracy {metrics.Accuracy:P2}, AUC {metrics.AreaUnderRocCurve:F3} ({foldTrain.Count:N0} train / {foldTest.Count:N0} test)");
    fold++;
}

Console.WriteLine($"Mean accuracy: {accuracies.Average():P2} (stddev {StdDev(accuracies):P2})");
Console.WriteLine($"Mean AUC:      {aucs.Average():F3} (stddev {StdDev(aucs):F3})");

// Final model for serving: train on everything, held-out test is just the last window
Console.WriteLine("\n--- Final model (trained on all data) ---");
var (train, test) = dataset.SplitByDate(TestWindowDays);
var model = new PriceTrendModel();
model.Train(train);
model.PrintEvaluation(test);
model.Save();

static double StdDev(IReadOnlyList<double> values)
{
    var mean = values.Average();
    var variance = values.Sum(v => (v - mean) * (v - mean)) / values.Count;
    return Math.Sqrt(variance);
}
