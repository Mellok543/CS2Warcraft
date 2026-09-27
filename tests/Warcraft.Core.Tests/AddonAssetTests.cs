using System.Text.RegularExpressions;

namespace Warcraft.Core.Tests;

/// <summary>
/// Every model the server spawns must exist as a ModelDoc source in assets/addon,
/// otherwise clients see ERROR models. Checked statically (Warcraft.Abilities needs CounterStrikeSharp).
/// </summary>
public sealed partial class AddonAssetTests
{
    [GeneratedRegex("\"(?<path>models/warcraft/[a-z_/]+\\.vmdl)\"")]
    private static partial Regex ModelPath();

    [Fact]
    public void EverySpawnedModelHasCompleteSources()
    {
        var root = FindRepositoryRoot();
        var registry = File.ReadAllText(Path.Combine(root, "src", "Warcraft.Abilities", "Game", "WarcraftModels.cs"));
        var models = ModelPath().Matches(registry).Select(x => x.Groups["path"].Value).Distinct().ToArray();

        Assert.True(models.Length >= 8, $"Only {models.Length} model paths found in WarcraftModels.");

        foreach (var model in models)
        {
            var vmdl = Path.Combine(root, "assets", "addon", model);
            Assert.True(File.Exists(vmdl), $"Missing model source {model}");

            var directory = Path.GetDirectoryName(vmdl)!;
            var name = Path.GetFileNameWithoutExtension(vmdl);
            var source = File.ReadAllText(vmdl);

            Assert.Contains($"{name}.obj", source);
            Assert.True(File.Exists(Path.Combine(directory, $"{name}.obj")), $"Missing mesh for {model}");
            Assert.True(File.Exists(Path.Combine(directory, $"{name}.vmat")), $"Missing material for {model}");
            Assert.True(File.Exists(Path.Combine(directory, $"{name}_color.png")), $"Missing texture for {model}");
        }
    }

    [Fact]
    public void GlowMaterialsAreNotFullyTransparent()
    {
        var root = FindRepositoryRoot();

        foreach (var relative in new[]
        {
            Path.Combine("assets", "addon", "models", "warcraft", "effects", "aura_ring", "aura_ring.vmat"),
            Path.Combine("assets", "addon", "models", "warcraft", "effects", "shield_bubble", "shield_bubble.vmat")
        })
        {
            var material = File.ReadAllText(Path.Combine(root, relative));
            Assert.Contains("F_TRANSLUCENT \"1\"", material);
            Assert.DoesNotContain(
                "g_vColorTint \"[1.000000 1.000000 1.000000 0.000000]\"",
                material);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CS2Warcraft.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
