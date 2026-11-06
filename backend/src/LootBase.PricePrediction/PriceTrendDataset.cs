using LootBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LootBase.PricePrediction;

/// <summary>Loads daily Skinport snapshots and turns them into labeled, time-ordered training samples.</summary>
public sealed class PriceTrendDataset
{
    private readonly IReadOnlyList<(DateOnly Date, TrainingSample Sample)> dated;

    private PriceTrendDataset(IReadOnlyList<(DateOnly Date, TrainingSample Sample)> dated)
    {
        this.dated = dated;
    }

    public IReadOnlyList<TrainingSample> All => dated.Select(d => d.Sample).ToList();

    private static float Return(decimal today, decimal lag) => lag == 0 ? 0f : (float)((today - lag) / lag);

    // Coefficient of variation (stddev / mean) over the given prices - scale-free,
    // same reasoning as Return: a 2€ and a 2000€ item shouldn't be compared on raw stddev
    private static float Volatility(IReadOnlyList<decimal> prices)
    {
        var mean = prices.Average();
        if (mean == 0)
        {
            return 0f;
        }

        var variance = prices.Sum(p => (p - mean) * (p - mean)) / prices.Count;
        return (float)((decimal)Math.Sqrt((double)variance) / mean);
    }

    /// <summary>Builds today's feature row for a single item, for a live prediction. Null if there isn't enough history yet.</summary>
    public static async Task<TrainingSample?> GetLatestFeaturesAsync(LootBaseDbContext db, string marketHashName, CancellationToken cancellationToken = default)
    {
        var lastFour = await db.ItemPriceSnapshots
            .AsNoTracking()
            .Where(s => s.MarketHashName == marketHashName && s.Source == "skinport" && s.Price != null)
            .OrderByDescending(s => s.CapturedDate)
            .Take(4)
            .Select(s => new { Price = s.Price!.Value, s.Quantity })
            .ToListAsync(cancellationToken);

        // Same "need 3 days of lag history" requirement as training
        if (lastFour.Count < 4)
        {
            return null;
        }

        return new TrainingSample
        {
            Price = (float)lastFour[0].Price,
            Return1 = Return(lastFour[0].Price, lastFour[1].Price),
            Return3 = Return(lastFour[0].Price, lastFour[3].Price),
            Quantity = lastFour[0].Quantity,
            Volatility = Volatility(lastFour.Select(x => x.Price).ToList()),
            QuantityTrend = Return(lastFour[0].Quantity, lastFour[3].Quantity)
        };
    }

    public static async Task<PriceTrendDataset> LoadAsync(LootBaseDbContext db, int horizonDays, CancellationToken cancellationToken = default)
    {
        // Project in the query instead of materializing full entities - this is ~1.8M rows
        var rows = await db.ItemPriceSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.Source == "skinport" && snapshot.Price != null)
            .Select(snapshot => new
            {
                snapshot.MarketHashName,
                snapshot.CapturedDate,
                Price = snapshot.Price!.Value,
                snapshot.Quantity
            })
            .ToListAsync(cancellationToken);

        Console.WriteLine($"Loaded {rows.Count:N0} daily snapshots.");

        var dated = new List<(DateOnly Date, TrainingSample Sample)>();

        foreach (var itemRows in rows.GroupBy(row => row.MarketHashName))
        {
            var series = itemRows.OrderBy(row => row.CapturedDate).ToList();
            var priceByDate = series.ToDictionary(row => row.CapturedDate, row => row.Price);

            // Start at 3 so the lag features below always exist
            for (var i = 3; i < series.Count; i++)
            {
                var today = series[i];

                // Only usable as training data if we can see horizonDays into the future
                if (!priceByDate.TryGetValue(today.CapturedDate.AddDays(horizonDays), out var futurePrice))
                {
                    continue;
                }

                dated.Add((today.CapturedDate, new TrainingSample
                {
                    Price = (float)today.Price,
                    Return1 = Return(today.Price, series[i - 1].Price),
                    Return3 = Return(today.Price, series[i - 3].Price),
                    Quantity = today.Quantity,
                    Volatility = Volatility([series[i - 3].Price, series[i - 2].Price, series[i - 1].Price, today.Price]),
                    QuantityTrend = Return(today.Quantity, series[i - 3].Quantity),
                    GoesUp = futurePrice > today.Price
                }));
            }
        }

        Console.WriteLine($"Built {dated.Count:N0} training samples.");
        return new PriceTrendDataset(dated);
    }

    /// <summary>
    /// Splits by date, not randomly: a random split would leak future prices of the
    /// same item into training and make the model look far better than it is.
    /// </summary>
    public (IReadOnlyList<TrainingSample> Train, IReadOnlyList<TrainingSample> Test) SplitByDate(int testWindowDays)
    {
        var cutoff = dated.Max(d => d.Date).AddDays(-testWindowDays);
        var train = dated.Where(d => d.Date <= cutoff).Select(d => d.Sample).ToList();
        var test = dated.Where(d => d.Date > cutoff).Select(d => d.Sample).ToList();

        Console.WriteLine($"Train: {train.Count:N0} (until {cutoff}), Test: {test.Count:N0}");
        return (train, test);
    }

    /// <summary>
    /// Rolling-origin time-series cross-validation: fold k tests on the k-th most
    /// recent testWindowDays-day window, trained only on everything before it.
    /// Never shuffles - a random/k-fold split would leak future prices into
    /// training and make every fold's score look better than it really is.
    /// </summary>
    public IEnumerable<(IReadOnlyList<TrainingSample> Train, IReadOnlyList<TrainingSample> Test)> RollingSplits(int testWindowDays, int folds)
    {
        var lastDate = dated.Max(d => d.Date);

        for (var fold = 0; fold < folds; fold++)
        {
            var testEnd = lastDate.AddDays(-testWindowDays * fold);
            var testStart = testEnd.AddDays(-testWindowDays);

            var train = dated.Where(d => d.Date <= testStart).Select(d => d.Sample).ToList();
            var test = dated.Where(d => d.Date > testStart && d.Date <= testEnd).Select(d => d.Sample).ToList();

            if (train.Count == 0 || test.Count == 0)
            {
                yield break;
            }

            yield return (train, test);
        }
    }

    // ponytail: no DB, no test project in this solution yet - plain asserts on
    // synthetic data are the cheapest thing that catches a broken lag/label
    // calculation. Promote to a real test project once one exists.
    public static void SelfCheck()
    {
        var dated = new List<(DateOnly Date, TrainingSample Sample)>();
        var rows = new (string Item, DateOnly Date, decimal Price, int Quantity)[]
        {
            ("A", new DateOnly(2026, 1, 1), 10m, 5),
            ("A", new DateOnly(2026, 1, 2), 11m, 5),
            ("A", new DateOnly(2026, 1, 3), 12m, 5),
            ("A", new DateOnly(2026, 1, 4), 13m, 5),
            ("A", new DateOnly(2026, 1, 11), 20m, 5) // +7 days from day 4 -> price went up
        };

        foreach (var itemRows in rows.GroupBy(r => r.Item))
        {
            var series = itemRows.OrderBy(r => r.Date).ToList();
            var priceByDate = series.ToDictionary(r => r.Date, r => r.Price);

            for (var i = 3; i < series.Count; i++)
            {
                var today = series[i];
                if (!priceByDate.TryGetValue(today.Date.AddDays(7), out var future))
                {
                    continue;
                }

                dated.Add((today.Date, new TrainingSample
                {
                    Price = (float)today.Price,
                    Return1 = Return(today.Price, series[i - 1].Price),
                    Return3 = Return(today.Price, series[i - 3].Price),
                    Quantity = today.Quantity,
                    Volatility = Volatility([series[i - 3].Price, series[i - 2].Price, series[i - 1].Price, today.Price]),
                    QuantityTrend = Return(today.Quantity, series[i - 3].Quantity),
                    GoesUp = future > today.Price
                }));
            }
        }

        if (dated.Count != 1)
        {
            throw new InvalidOperationException($"SelfCheck failed: expected 1 sample, got {dated.Count}.");
        }

        // day 4: price 13, lag1 12 -> +8.33%, lag3 10 -> +30%
        // volatility over [10,11,12,13]: mean 11.5, stddev ~1.118 -> ~0.0972
        // quantity stays 5 the whole series -> QuantityTrend 0
        var sample = dated[0].Sample;
        if (sample.Price != 13f ||
            Math.Abs(sample.Return1 - 1f / 12f) > 0.001f ||
            Math.Abs(sample.Return3 - 0.3f) > 0.001f ||
            Math.Abs(sample.Volatility - 0.0972f) > 0.001f ||
            sample.QuantityTrend != 0f ||
            !sample.GoesUp)
        {
            throw new InvalidOperationException("SelfCheck failed: feature/label values don't match the synthetic series.");
        }
    }
}
