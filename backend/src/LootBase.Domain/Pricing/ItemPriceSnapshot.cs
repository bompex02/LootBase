namespace LootBase.Domain.Pricing;

public sealed class ItemPriceSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string MarketHashName { get; set; } = string.Empty;

    public string Currency { get; set; } = "EUR";

    public DateOnly CapturedDate { get; set; }

    public decimal? MinPrice { get; set; }

    public decimal? MedianPrice { get; set; }

    public decimal? MeanPrice { get; set; }

    public decimal? MaxPrice { get; set; }

    public int Quantity { get; set; }

    // "skinport" (recorded organically from live pricing lookups) or "steam"
    // (backfilled from Steam Market's price history)
    public string Source { get; set; } = "skinport";
}
