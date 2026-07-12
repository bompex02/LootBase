using System.Net;
using System.Text.Json;
using LootBase.Application.Abstractions.Inventory;
using LootBase.Domain.Games;

namespace LootBase.Infrastructure.Inventory;

public sealed class Cs2SteamInventoryProvider(HttpClient httpClient) : IInventoryProvider
{
    // Exact, stable messages so callers (see PlayerEndpoints) can match on them
    // to distinguish known Steam-API conditions from unexpected internal bugs,
    // without needing a dedicated exception type.
    public const string InventoryPrivateMessage =
        "Steam inventory is not public. Set your Steam inventory privacy to Public and try again.";
    public const string RateLimitMessage = "Steam rate limit reached. Wait a bit and try again.";
    public const string InventoryRequestFailedMessage = "Steam inventory request failed.";

    private const int ContextId = 2;
    private const int PageSize = 5000;
    private const string IconBaseUrl = "https://community.cloudflare.steamstatic.com/economy/image/";

    public int AppId => SteamGames.CounterStrike2;

    public async Task<IReadOnlyCollection<InventoryAsset>> GetInventoryAsync(
        string steamId64,
        CancellationToken cancellationToken)
    {
        var assets = new List<SteamAsset>();
        var descriptionsByKey = new Dictionary<string, SteamDescription>();
        string? startAssetId = null;

        do
        {
            var page = await GetInventoryPageAsync(steamId64, startAssetId, cancellationToken);
            assets.AddRange(page.Assets);

            foreach (var description in page.Descriptions)
            {
                descriptionsByKey[CreateDescriptionKey(description.ClassId, description.InstanceId)] = description;
            }

            startAssetId = page.MoreItems ? page.LastAssetId : null;
        }
        while (!string.IsNullOrWhiteSpace(startAssetId));

        return assets
            .Select(asset =>
            {
                descriptionsByKey.TryGetValue(
                    CreateDescriptionKey(asset.ClassId, asset.InstanceId),
                    out var description);

                var marketHashName = description?.MarketHashName ??
                    description?.Name ??
                    $"{asset.ClassId}_{asset.InstanceId}";
                var displayName = description?.Name ?? marketHashName;

                return new InventoryAsset(
                    asset.AssetId,
                    asset.ClassId,
                    asset.InstanceId,
                    marketHashName,
                    displayName,
                    BuildIconUrl(description?.IconUrl),
                    GetTag(description, "Type"),
                    GetTag(description, "Exterior"),
                    GetTag(description, "Rarity"),
                    int.TryParse(asset.Amount, out var amount) ? Math.Max(1, amount) : 1);
            })
            .ToList();
    }

    private async Task<SteamInventoryPage> GetInventoryPageAsync(
        string steamId64,
        string? startAssetId,
        CancellationToken cancellationToken)
    {
        var uri = BuildInventoryUri(steamId64, startAssetId);
        using var response = await httpClient.GetAsync(uri, cancellationToken);

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException(InventoryPrivateMessage);
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new InvalidOperationException(RateLimitMessage);
        }

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        if (root.TryGetProperty("success", out var successElement) &&
            successElement.ValueKind == JsonValueKind.Number &&
            successElement.GetInt32() == 0)
        {
            var error = root.TryGetProperty("Error", out var errorElement)
                ? errorElement.GetString()
                : null;

            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error) ? InventoryRequestFailedMessage : $"{InventoryRequestFailedMessage} {error}");
        }

        return new SteamInventoryPage(
            ReadAssets(root),
            ReadDescriptions(root),
            root.TryGetProperty("more_items", out var moreItemsElement) &&
                moreItemsElement.ValueKind is JsonValueKind.True,
            root.TryGetProperty("last_assetid", out var lastAssetIdElement)
                ? lastAssetIdElement.GetString()
                : null);
    }

    private static Uri BuildInventoryUri(string steamId64, string? startAssetId)
    {
        /*var query = $"l=english&count={PageSize}";
        if (!string.IsNullOrWhiteSpace(startAssetId))
        {
            query += $"&start_assetid={Uri.EscapeDataString(startAssetId)}";
        }*/

        return new Uri(
            $"https://steamcommunity.com/inventory/{Uri.EscapeDataString(steamId64)}/{SteamGames.CounterStrike2}/{ContextId}");
    }

    private static IReadOnlyList<SteamAsset> ReadAssets(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assetsElement) ||
            assetsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return assetsElement
            .EnumerateArray()
            .Select(asset => new SteamAsset(
                ReadRequiredString(asset, "assetid"),
                ReadRequiredString(asset, "classid"),
                ReadOptionalString(asset, "instanceid") ?? "0",
                ReadOptionalString(asset, "amount") ?? "1"))
            .ToList();
    }

    private static IReadOnlyList<SteamDescription> ReadDescriptions(JsonElement root)
    {
        if (!root.TryGetProperty("descriptions", out var descriptionsElement) ||
            descriptionsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return descriptionsElement
            .EnumerateArray()
            .Select(description => new SteamDescription(
                ReadRequiredString(description, "classid"),
                ReadOptionalString(description, "instanceid") ?? "0",
                ReadOptionalString(description, "market_hash_name"),
                ReadOptionalString(description, "name"),
                ReadOptionalString(description, "icon_url"),
                ReadTags(description)))
            .ToList();
    }

    private static IReadOnlyDictionary<string, string> ReadTags(JsonElement description)
    {
        if (!description.TryGetProperty("tags", out var tagsElement) ||
            tagsElement.ValueKind != JsonValueKind.Array)
        {
            return new Dictionary<string, string>();
        }

        return tagsElement
            .EnumerateArray()
            .Select(tag => new
            {
                Category = ReadOptionalString(tag, "category") ??
                    ReadOptionalString(tag, "localized_category_name"),
                Name = ReadOptionalString(tag, "localized_tag_name") ??
                    ReadOptionalString(tag, "name") ??
                    ReadOptionalString(tag, "internal_name")
            })
            .Where(tag => !string.IsNullOrWhiteSpace(tag.Category) && !string.IsNullOrWhiteSpace(tag.Name))
            .GroupBy(tag => tag.Category!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().Name!,
                StringComparer.OrdinalIgnoreCase);
    }

    private static string? GetTag(SteamDescription? description, string category)
    {
        return description?.Tags.TryGetValue(category, out var value) == true ? value : null;
    }

    private static string? BuildIconUrl(string? iconUrl)
    {
        return string.IsNullOrWhiteSpace(iconUrl) ? null : $"{IconBaseUrl}{iconUrl}";
    }

    private static string CreateDescriptionKey(string classId, string instanceId)
    {
        return $"{classId}:{instanceId}";
    }

    private static string ReadRequiredString(JsonElement element, string propertyName)
    {
        return ReadOptionalString(element, propertyName) ??
            throw new InvalidOperationException($"Steam inventory response is missing '{propertyName}'.");
    }

    private static string? ReadOptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null
        };
    }

    private sealed record SteamInventoryPage(
        IReadOnlyList<SteamAsset> Assets,
        IReadOnlyList<SteamDescription> Descriptions,
        bool MoreItems,
        string? LastAssetId);

    private sealed record SteamAsset(
        string AssetId,
        string ClassId,
        string InstanceId,
        string Amount);

    private sealed record SteamDescription(
        string ClassId,
        string InstanceId,
        string? MarketHashName,
        string? Name,
        string? IconUrl,
        IReadOnlyDictionary<string, string> Tags);
}
