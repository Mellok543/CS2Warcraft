using System.Text.Json;
using Warcraft.Api.Abilities;
using Warcraft.Core.Abilities;

namespace Warcraft.Core.Tests;

public sealed class AbilityDescriptionTests
{
    private const ulong Player = 7;

    private static JsonElement Config(string json) => JsonDocument.Parse(json).RootElement;

    [Theory]
    [InlineData("Шанс {chance%}", 1, "Шанс 8%")]
    [InlineData("Шанс {chance%}", 3, "Шанс 20%")]
    [InlineData("x{damageMultiplier}", 2, "x1.4")]
    [InlineData("{damage} урона", 2, "35 урона")]
    [InlineData("каждые {interval|1} с", 1, "каждые 1 с")]
    [InlineData("{missing}", 1, "?")]
    public void PlaceholdersUseLevelValues(string template, int level, string expected)
    {
        var config = Config("""{ "chance": [0.08, 0.14, 0.20], "damageMultiplier": [1.25, 1.40, 1.60], "damage": 35 }""");
        Assert.Equal(expected, AbilityDescriptionFormatter.Format(template, config, level));
    }

    [Fact]
    public void RaceDescriptionWithUnknownKeyIsRejected()
    {
        var core = new TestCore();
        var race = TestCore.ParseRace("""
            { "id": "r", "name": "R", "abilities": [
              { "id": "a", "description": "{chanse%}", "config": { "chance": 0.1 } } ] }
            """);

        var result = core.Races.ReplaceCatalog([race], "test");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("chanse"));
    }

    [Fact]
    public void StatusRendersCurrentNextAndConditions()
    {
        var core = new TestCore();
        core.LoadRaces("""
            { "id": "r", "name": "R", "maxLevel": 10, "abilities": [
              { "id": "crit", "maxLevel": 3, "config": { "chance": [0.1, 0.2, 0.3] },
                "conditions": { "weaponTypes": ["rifle"], "minHealthPercent": 0.5 } },
              { "id": "custom", "description": "Своё: {chance%}", "config": { "chance": 0.5 } } ] }
            """);
        core.Registrations.Register(new AbilityRegistration("crit", "test", AbilityKind.Passive, "Шанс {chance%}"));
        core.AddPlayer(Player, "r", 5, ("crit", 1));

        var statuses = core.Abilities.GetPlayerAbilities(Player);
        var crit = Assert.Single(statuses, x => x.AbilityId == "crit");
        var custom = Assert.Single(statuses, x => x.AbilityId == "custom");

        Assert.Equal("Шанс 10%", crit.Description);
        Assert.Equal("Шанс 20%", crit.NextLevelDescription);
        Assert.Equal("оружие: винтовка; HP ≥ 50%", crit.ConditionsDescription);
        Assert.Equal("Своё: 50%", custom.Description);

        var preview = Assert.Single(core.Abilities.GetRaceAbilities("r"), x => x.AbilityId == "crit");
        Assert.Equal("Шанс 30%", preview.MaxLevelDescription);
    }
}
