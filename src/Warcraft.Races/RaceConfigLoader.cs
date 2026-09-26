using System.Text.Json;
using CounterStrikeSharp.API;
using Warcraft.Api.Races;

namespace Warcraft.Races;

internal sealed class RaceConfigLoader
{
    private readonly JsonSerializerOptions _options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public string RaceDirectory =>
        Path.Combine(Server.GameDirectory, "configs", "warcraft", "races");

    public void EnsureDefaultConfigs(string moduleDirectory)
    {
        Directory.CreateDirectory(RaceDirectory);

        if (Directory.EnumerateFiles(RaceDirectory, "*.json").Any())
            return;

        var defaults = Path.Combine(moduleDirectory, "defaults", "races");
        if (!Directory.Exists(defaults))
            return;

        foreach (var source in Directory.EnumerateFiles(defaults, "*.json"))
        {
            var destination = Path.Combine(RaceDirectory, Path.GetFileName(source));
            File.Copy(source, destination, overwrite: false);
        }
    }

    public RaceLoadResult LoadAll()
    {
        Directory.CreateDirectory(RaceDirectory);

        var races = new List<RaceDefinition>();
        var errors = new List<string>();

        foreach (var file in Directory
                     .EnumerateFiles(RaceDirectory, "*.json")
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var json = File.ReadAllText(file);
                var race = JsonSerializer.Deserialize<RaceDefinition>(json, _options);

                if (race is null)
                {
                    errors.Add($"{Path.GetFileName(file)}: empty race definition.");
                    continue;
                }

                races.Add(race);
            }
            catch (Exception exception) when (
                exception is JsonException or IOException or UnauthorizedAccessException)
            {
                errors.Add($"{Path.GetFileName(file)}: {exception.Message}");
            }
        }

        return new RaceLoadResult(races, errors);
    }
}

internal sealed record RaceLoadResult(
    IReadOnlyCollection<RaceDefinition> Races,
    IReadOnlyList<string> Errors);
