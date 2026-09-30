using System.Text.Json;
using CounterStrikeSharp.API;
using Warcraft.Shop.Effects;

namespace Warcraft.Shop;

internal sealed record ShopItemDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public int Price { get; init; }

    /// <summary>0 = unlimited.</summary>
    public int MaxPerRound { get; init; }

    public bool RequiresAlive { get; init; } = true;
    public JsonElement Effect { get; init; }

    public string EffectType
        => Effect.ValueKind == JsonValueKind.Object && Effect.TryGetProperty("type", out var type)
            ? type.GetString() ?? string.Empty
            : string.Empty;
}

internal sealed record ShopFile
{
    public IReadOnlyList<ShopItemDefinition> Items { get; init; } = [];
}

internal sealed record ShopLoadResult(IReadOnlyList<ShopItemDefinition> Items, IReadOnlyList<string> Errors, string Path);

/// <summary>Loads and validates configs/warcraft/shop.json. Invalid items are skipped.</summary>
internal sealed class ShopCatalogLoader(ShopEffectRegistry effects)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static string ConfigPath =>
        Path.Combine(Server.GameDirectory, "configs", "warcraft", "shop.json");

    public void EnsureDefault(string moduleDirectory)
    {
        if (File.Exists(ConfigPath))
            return;

        var source = Path.Combine(moduleDirectory, "defaults", "shop.json");
        if (!File.Exists(source))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.Copy(source, ConfigPath, overwrite: false);
    }

    public ShopLoadResult Load(string moduleDirectory)
    {
        var path = ResolveConfigPath(moduleDirectory);
        if (!File.Exists(path))
            return new([], [$"{path} not found."], path);

        ShopFile? file;
        try
        {
            file = JsonSerializer.Deserialize<ShopFile>(File.ReadAllText(path), Options);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new([], [$"shop.json ({path}): {exception.Message}"], path);
        }

        var items = new List<ShopItemDefinition>();
        var errors = new List<string>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in file?.Items ?? [])
        {
            var error = Validate(item, ids);
            if (error is null)
                items.Add(item);
            else
                errors.Add($"shop item '{item.Id}': {error}");
        }

        return new(items, errors, path);
    }

    public string ResolveConfigPath(string moduleDirectory)
    {
        var runtime = ConfigPath;
        var packaged = Path.Combine(moduleDirectory, "defaults", "shop.json");

        if (!File.Exists(runtime))
            return packaged;
        if (!File.Exists(packaged))
            return runtime;

        return File.GetLastWriteTimeUtc(packaged) > File.GetLastWriteTimeUtc(runtime)
            ? packaged
            : runtime;
    }

    private string? Validate(ShopItemDefinition item, HashSet<string> ids)
    {
        if (string.IsNullOrWhiteSpace(item.Id))
            return "id is required.";
        if (!ids.Add(item.Id))
            return "duplicate id.";
        if (string.IsNullOrWhiteSpace(item.Name))
            return "name is required.";
        if (item.Price < 0 || item.MaxPerRound < 0)
            return "price and maxPerRound must be >= 0.";

        var effect = effects.Get(item.EffectType);
        return effect is null
            ? $"unknown effect type '{item.EffectType}'. Known: {string.Join(", ", effects.Types)}."
            : effect.Validate(item.Effect);
    }
}
