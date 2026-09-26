using System.Text.Json;
using CounterStrikeSharp.API;

namespace Warcraft.Core;

internal sealed record CoreConfig
{
    public int KillXp { get; init; } = 25;
    public int HeadshotBonusXp { get; init; } = 10;
    public int AutosaveDelayMilliseconds { get; init; } = 1000;
    public int GameTickIntervalMilliseconds { get; init; } = 100;

    public static string ConfigPath =>
        Path.Combine(Server.GameDirectory, "configs", "warcraft", "core.json");

    public static CoreConfig LoadOrCreate()
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
            var defaults = new CoreConfig();
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(defaults, options));
            return defaults;
        }

        var json = File.ReadAllText(ConfigPath);
        return JsonSerializer.Deserialize<CoreConfig>(json, options)
            ?? throw new InvalidOperationException("core.json is empty or invalid.");
    }
}
