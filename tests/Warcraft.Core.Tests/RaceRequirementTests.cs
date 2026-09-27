namespace Warcraft.Core.Tests;

public sealed class RaceRequirementTests
{
    private const ulong Player = 21;

    private const string Orc = """{ "id": "orc", "name": "Орк", "maxLevel": 10 }""";
    private const string Human = """{ "id": "human", "name": "Человек", "maxLevel": 10 }""";
    private const string Paladin = """
        { "id": "paladin", "name": "Паладин", "maxLevel": 10,
          "requirements": { "totalLevel": 8, "races": { "human": 5 } } }
        """;

    private static TestCore Create()
    {
        var core = new TestCore();
        core.LoadRaces(Orc, Human, Paladin);
        core.Players.Upsert(Player, "p");
        return core;
    }

    [Fact]
    public void LockedRaceCannotBeSelectedAndReportsProgress()
    {
        var core = Create();
        core.Races.SelectRace(Player, "human", "test");
        core.Progress.SetRaceLevel(Player, "human", 4, "test");

        var availability = core.Races.GetAvailability(Player, "paladin");
        var result = core.Races.SelectRace(Player, "paladin", "test");

        Assert.False(availability.IsAvailable);
        Assert.Equal([(4L, 8L), (4L, 5L)], availability.Requirements.Select(x => (x.Current, x.Required)));
        Assert.False(result.Success);
        Assert.Contains("закрыта", result.Message);
    }

    [Fact]
    public void RaceOpensWhenAllRequirementsAreMet()
    {
        var core = Create();
        core.Races.SelectRace(Player, "orc", "test");
        core.Progress.SetRaceLevel(Player, "orc", 3, "test");
        core.Races.SelectRace(Player, "human", "test");
        core.Progress.SetRaceLevel(Player, "human", 5, "test");

        Assert.True(core.Races.GetAvailability(Player, "paladin").IsAvailable);
        Assert.True(core.Races.SelectRace(Player, "paladin", "test").Success);
    }

    [Fact]
    public void UnlockedRaceStaysAvailableAndForceBypassesRequirements()
    {
        var core = Create();

        Assert.True(core.Races.SelectRace(Player, "paladin", "admin", force: true).Success);
        core.Races.SelectRace(Player, "orc", "test");

        var availability = core.Races.GetAvailability(Player, "paladin");
        Assert.True(availability.AlreadyUnlocked);
        Assert.True(availability.IsAvailable);
    }

    [Fact]
    public void PlaytimeRequirementBlocksRaceUntilRequiredHours()
    {
        var core = new TestCore();
        core.LoadRaces(
            Orc,
            """{ "id": "final", "name": "Финальная", "requirements": { "playtimeHours": 70 } }""");

        var player = core.Players.Upsert(Player, "p");
        player.Stats.SessionStartedAt = core.Time.Now;

        core.Time.Now = core.Time.Now.AddHours(69).AddMinutes(59);
        Assert.False(core.Races.GetAvailability(Player, "final").IsAvailable);

        core.Time.Now = core.Time.Now.AddMinutes(1);
        Assert.True(core.Races.GetAvailability(Player, "final").IsAvailable);
    }

    [Theory]
    [InlineData("""{ "races": { "elf": 3 } }""", "unknown race")]
    [InlineData("""{ "races": { "human": 11 } }""", "allowed 1..10")]
    [InlineData("""{ "races": { "paladin": 1 } }""", "cannot require itself")]
    [InlineData("""{ "totalLevel": -1 }""", "negative")]
    [InlineData("""{ "playtimeHours": -1 }""", "negative")]
    public void InvalidRequirementsRejectTheCatalog(string requirements, string error)
    {
        var core = new TestCore();
        var paladin = TestCore.ParseRace(
            $$"""{ "id": "paladin", "name": "Паладин", "requirements": {{requirements}} }""");

        var result = core.Races.ReplaceCatalog([TestCore.ParseRace(Human), paladin], "test");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains(error));
    }
}
