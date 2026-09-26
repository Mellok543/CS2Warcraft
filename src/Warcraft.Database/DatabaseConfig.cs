using System.Text.Json;
using CounterStrikeSharp.API;

namespace Warcraft.Database;

internal sealed record DatabaseConfig
{
    public bool Enabled { get; init; }
    public string Host { get; init; } = "127.0.0.1";
    public uint Port { get; init; } = 3306;
    public string Database { get; init; } = "warcraft";
    public string User { get; init; } = "warcraft";
    public string Password { get; init; } = "change-me";
    public uint MinimumPoolSize { get; init; } = 1;
    public uint MaximumPoolSize { get; init; } = 20;
    public uint ConnectionTimeoutSeconds { get; init; } = 10;

    public static string ConfigPath =>
        Path.Combine(Server.GameDirectory, "configs", "warcraft", "database.json");

    public static DatabaseConfig LoadOrCreate()
    {
        var path = ConfigPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        if (!File.Exists(path))
        {
            var defaults = new DatabaseConfig();
            File.WriteAllText(path, JsonSerializer.Serialize(defaults, options));
            return defaults;
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<DatabaseConfig>(json, options)
            ?? throw new InvalidOperationException("database.json is empty or invalid.");
    }
}
