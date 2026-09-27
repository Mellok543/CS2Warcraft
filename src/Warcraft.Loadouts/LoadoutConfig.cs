using System.Text.Json;
using CounterStrikeSharp.API;

namespace Warcraft.Loadouts;

internal sealed record RaceLoadout
{
    public IReadOnlyList<string> Items { get; init; } = [];
}

internal sealed record LoadoutConfig
{
    public IReadOnlyDictionary<string, RaceLoadout> Races { get; init; } =
        new Dictionary<string, RaceLoadout>(StringComparer.OrdinalIgnoreCase);

    public static string ConfigPath =>
        Path.Combine(Server.GameDirectory, "configs", "warcraft", "race_loadouts.json");

    public static LoadoutConfig LoadOrCreate(string moduleDirectory)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);

        var defaults = Path.Combine(moduleDirectory, "defaults", "race_loadouts.json");
        if (!File.Exists(ConfigPath) && File.Exists(defaults))
            File.Copy(defaults, ConfigPath);

        if (!File.Exists(ConfigPath))
            return new LoadoutConfig();

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        return JsonSerializer.Deserialize<LoadoutConfig>(File.ReadAllText(ConfigPath), options)
               ?? new LoadoutConfig();
    }
}
