using Warcraft.Api.Abilities;
using Warcraft.Core.Conditions;

namespace Warcraft.Core.Tests;

public sealed class AbilityUsabilityTests
{
    private const ulong Player = 76561198000000001;

    private const string Race = """
        {
          "id": "hunter", "name": "Hunter", "maxLevel": 10,
          "abilities": [
            { "id": "rifle_crit", "maxLevel": 3, "unlockLevel": 3,
              "config": { "chance": [0.1, 0.2, 0.3] },
              "conditions": { "weaponTypes": ["rifle", "smg"], "minHealthPercent": 0.5 } },
            { "id": "plain", "maxLevel": 1, "unlockLevel": 1, "config": {} }
          ]
        }
        """;

    private static TestCore Create(int raceLevel, int abilityLevel)
    {
        var core = new TestCore();
        core.LoadRaces(Race);
        core.AddPlayer(Player, "hunter", raceLevel, ("rifle_crit", abilityLevel));
        return core;
    }

    [Fact]
    public void UsableWhenLearnedUnlockedAndConditionsPass()
    {
        var core = Create(raceLevel: 3, abilityLevel: 2);

        var ability = core.Abilities.GetUsableAbility(Player, "rifle_crit");

        Assert.NotNull(ability);
        Assert.Equal(2, ability.Level);
        Assert.Equal("hunter", ability.RaceId);
    }

    [Fact]
    public void NotUsableWhenNotLearned()
    {
        var core = Create(raceLevel: 3, abilityLevel: 0);
        Assert.Null(core.Abilities.GetUsableAbility(Player, "rifle_crit"));
    }

    [Fact]
    public void NotUsableWhenRaceLevelDroppedBelowUnlock()
    {
        var core = Create(raceLevel: 3, abilityLevel: 1);
        core.Progress.SetRaceLevel(Player, "hunter", 2, "test");

        Assert.Null(core.Abilities.GetUsableAbility(Player, "rifle_crit"));
    }

    [Theory]
    [InlineData(100, WeaponCategories.Rifle, true)]
    [InlineData(100, WeaponCategories.Smg, true)]
    [InlineData(100, WeaponCategories.Knife, false)]
    [InlineData(40, WeaponCategories.Rifle, false)]
    public void ConditionsAreEvaluatedAgainstOwnerState(int health, string weapon, bool usable)
    {
        var core = Create(raceLevel: 3, abilityLevel: 1);
        core.Combat.States[Player] = new PlayerCombatState(true, health, 100, true, null, weapon);

        Assert.Equal(usable, core.Abilities.GetUsableAbility(Player, "rifle_crit") is not null);
    }

    [Fact]
    public void StatusReportsCoreComputedUpgradeState()
    {
        var core = Create(raceLevel: 2, abilityLevel: 0);

        var statuses = core.Abilities.GetPlayerAbilities(Player);
        var crit = Assert.Single(statuses, x => x.AbilityId == "rifle_crit");

        Assert.False(crit.IsUnlocked);
        Assert.Equal(AbilityUpgradeBlock.RaceLevelTooLow, crit.UpgradeBlock);
        Assert.False(crit.HandlerRegistered);
    }
}
