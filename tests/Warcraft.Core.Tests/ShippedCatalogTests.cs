using System.Text.Json;
using System.Text.RegularExpressions;

namespace Warcraft.Core.Tests;

/// <summary>
/// Static check of the shipped race JSON against the handler sources in
/// Warcraft.Abilities (which depend on CounterStrikeSharp and cannot be loaded here).
/// </summary>
public sealed partial class ShippedCatalogTests
{
    [GeneratedRegex(@"internal (?:sealed |abstract )+class (?<name>\w+)(?:<[^>]*>)?(?:\([^)]*\))?\s*(?::\s*(?<base>\w+))?")]
    private static partial Regex ClassDeclaration();

    [GeneratedRegex("override string Id => \"(?<id>[a-z_]+)\"")]
    private static partial Regex IdDeclaration();

    [Fact]
    public void EveryShippedAbilityHasAHandlerAndUltimatesAreActivatable()
    {
        var (handlers, activatable) = ReadHandlers();
        var errors = new List<string>();

        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "races"), "*.json"))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var race = document.RootElement.GetProperty("id").GetString();

            if (document.RootElement.TryGetProperty("abilities", out var abilities))
            {
                foreach (var ability in abilities.EnumerateArray())
                {
                    var id = ability.GetProperty("id").GetString()!;
                    if (!handlers.Contains(id))
                        errors.Add($"{race}: ability '{id}' has no handler");
                }
            }

            if (document.RootElement.TryGetProperty("ultimate", out var ultimate))
            {
                var id = ultimate.GetProperty("id").GetString()!;
                if (!handlers.Contains(id))
                    errors.Add($"{race}: ultimate '{id}' has no handler");
                else if (!activatable.Contains(id))
                    errors.Add($"{race}: ultimate '{id}' is a passive mechanic");
            }
        }

        Assert.True(handlers.Count > 40, $"Only {handlers.Count} handlers found; source scan is broken.");
        Assert.Empty(errors);
    }

    private static (HashSet<string> Handlers, HashSet<string> Activatable) ReadHandlers()
    {
        var root = FindRepositoryRoot();
        var bases = new Dictionary<string, string?>();
        var ids = new Dictionary<string, string>();

        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src", "Warcraft.Abilities"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                continue;

            var source = File.ReadAllText(file);
            var classes = ClassDeclaration().Matches(source).ToArray();

            for (var i = 0; i < classes.Length; i++)
            {
                var name = classes[i].Groups["name"].Value;
                bases[name] = classes[i].Groups["base"].Success ? classes[i].Groups["base"].Value : null;

                var end = i + 1 < classes.Length ? classes[i + 1].Index : source.Length;
                var id = IdDeclaration().Match(source[classes[i].Index..end]);
                if (id.Success)
                    ids[name] = id.Groups["id"].Value;
            }
        }

        bool IsActivatable(string name)
        {
            for (var current = name; current is not null; current = bases.GetValueOrDefault(current))
            {
                if (current == "ActiveAbilityHandler")
                    return true;
            }

            return false;
        }

        return (
            ids.Values.ToHashSet(),
            ids.Where(x => IsActivatable(x.Key)).Select(x => x.Value).ToHashSet());
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CS2Warcraft.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
