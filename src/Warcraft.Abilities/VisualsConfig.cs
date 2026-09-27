using System.Text.Json;
using CounterStrikeSharp.API;

namespace Warcraft.Abilities;

/// <summary>configs/warcraft/visuals.json — decorative ability models from the Warcraft UI addon.</summary>
internal sealed record VisualsConfig
{
    /// <summary>
    /// Spawn totem/roots/shield/aura models. Requires the Warcraft UI Workshop addon to be
    /// mounted on the server and on clients; set to false to fall back to beams only.
    /// </summary>
    public bool Models { get; init; } = true;

    public static string ConfigPath =>
        Path.Combine(Server.GameDirectory, "configs", "warcraft", "visuals.json");

    public static VisualsConfig LoadOrCreate()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        if (!File.Exists(ConfigPath))
        {
            var defaults = new VisualsConfig();
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(defaults, options));
            return defaults;
        }

        return JsonSerializer.Deserialize<VisualsConfig>(File.ReadAllText(ConfigPath), options)
               ?? throw new InvalidOperationException("visuals.json is empty or invalid.");
    }
}
