using System.Text.Json;
using CounterStrikeSharp.API;

namespace Warcraft.Menu;

internal sealed class WarcraftMenuConfig
{
    public bool UseBindNavigation { get; set; } = true;
    public bool ShowNavigationHints { get; set; } = true;

    public static WarcraftMenuConfig LoadOrCreate()
    {
        var directory = Path.Combine(Server.GameDirectory, "csgo", "configs", "warcraft");
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, "menu.json");
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        if (!File.Exists(path))
        {
            var created = new WarcraftMenuConfig();
            File.WriteAllText(path, JsonSerializer.Serialize(created, options));
            return created;
        }

        try
        {
            return JsonSerializer.Deserialize<WarcraftMenuConfig>(
                       File.ReadAllText(path),
                       options)
                   ?? new WarcraftMenuConfig();
        }
        catch
        {
            return new WarcraftMenuConfig();
        }
    }
}
