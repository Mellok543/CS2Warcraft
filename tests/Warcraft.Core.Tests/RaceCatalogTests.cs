namespace Warcraft.Core.Tests;

public sealed class RaceCatalogTests
{
    private const string ValidRace = """
        {
          "id": "test",
          "name": "Test",
          "maxLevel": 10,
          "abilities": [
            { "id": "critical_strike", "maxLevel": 3, "unlockLevel": 1,
              "config": { "chance": [0.1, 0.2, 0.3] },
              "conditions": { "weaponTypes": ["rifle"] } }
          ],
          "ultimate": { "id": "chain_lightning", "maxLevel": 1, "unlockLevel": 6,
                        "config": { "damage": 35, "cooldown": 25 } }
        }
        """;

    [Fact]
    public void ShippedRaceConfigsCompile()
    {
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "races"), "*.json");
        Assert.NotEmpty(files);

        var core = new TestCore();
        core.LoadRaces(files.Select(File.ReadAllText).ToArray());

        Assert.Equal(files.Length, core.Races.GetAll().Count);
    }

    [Theory]
    [InlineData("""{ "unknownCondition": true }""")]
    [InlineData("""{ "weaponTypes": ["laser"] }""")]
    [InlineData("""{ "weaponTypes": "rifle" }""")]
    [InlineData("""{ "minHealthPercent": 1.5 }""")]
    [InlineData("""{ "requiresGrounded": "yes" }""")]
    [InlineData("""[]""")]
    public void InvalidConditionsRejectWholeCatalogAndKeepPrevious(string conditions)
    {
        var core = new TestCore();
        core.LoadRaces(ValidRace);

        var broken = ValidRace
            .Replace("\"id\": \"test\"", "\"id\": \"broken\"")
            .Replace("""{ "weaponTypes": ["rifle"] }""", conditions);

        var result = core.Races.ReplaceCatalog(
            [TestCore.ParseRace(broken), TestCore.ParseRace(ValidRace.Replace("\"test\"", "\"other\""))],
            "test");

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
        Assert.NotNull(core.Races.Get("test"));
        Assert.Null(core.Races.Get("other"));
    }

    [Fact]
    public void NegativeCooldownIsRejected()
    {
        var core = new TestCore();
        var race = TestCore.ParseRace(ValidRace.Replace("\"cooldown\": 25", "\"cooldown\": -1"));

        Assert.False(core.Races.ReplaceCatalog([race], "test").Success);
    }

    [Fact]
    public void UltimateCannotReuseRegularAbilityId()
    {
        var core = new TestCore();
        var race = TestCore.ParseRace(ValidRace.Replace("\"id\": \"chain_lightning\"", "\"id\": \"critical_strike\""));

        var result = core.Races.ReplaceCatalog([race], "test");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("reuses ability"));
    }
}
