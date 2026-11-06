namespace LootBase.PricePrediction;

/// <summary>One labeled row for the ML.NET pipeline:
/// today's price features plus whether it rose within the horizon
/// </summary>
public sealed class TrainingSample
{
    // features below, float because ML.NET concatenates them into one numeric vector

    // price "today"
    public float Price { get; init; }

    // % change from yesterday to today, e.g. 0.05 = +5%. Relative, not absolute,
    // so a 2€ skin and a 2000€ knife are on the same scale - that's the whole
    // point of a return instead of a raw lag price
    public float Return1 { get; init; }

    // % change from 3 days ago to today
    public float Return3 { get; init; }

    // units sold "today"
    public float Quantity { get; init; }

    // how "jumpy" the last 4 prices are: stddev / mean. 0 = dead flat,
    // higher = noisier - a stable item and a swingy item shouldn't be judged
    // on the same price-momentum scale
    public float Volatility { get; init; }

    // % change in volume from 3 days ago to today - rising interest often
    // leads a price move, same idea as Return1/Return3 but for Quantity
    public float QuantityTrend { get; init; }

    // label: true if the price 7 days after today is higher. Unknown (false)
    public bool GoesUp { get; init; }
}
