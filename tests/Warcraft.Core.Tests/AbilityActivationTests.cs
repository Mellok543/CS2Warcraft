using Warcraft.Api.Abilities;
using Warcraft.Api.Events;

namespace Warcraft.Core.Tests;

public sealed class AbilityActivationTests
{
    private const ulong Player = 76561198000000002;

    private const string Race = """
        {
          "id": "shaman", "name": "Shaman", "maxLevel": 10,
          "abilities": [
            { "id": "passive_one", "maxLevel": 1, "unlockLevel": 1 },
            { "id": "blink", "maxLevel": 2, "unlockLevel": 1, "config": { "cooldown": [10, 6] } }
          ],
          "ultimate": { "id": "bolt", "maxLevel": 2, "unlockLevel": 6, "config": { "cooldown": [25, 15] } }
        }
        """;

    private static TestCore Create(int raceLevel, int ultimateLevel)
    {
        var core = new TestCore();
        core.LoadRaces(Race);
        core.Registrations.Register(new AbilityRegistration("bolt", "test", AbilityKind.Ultimate));
        core.Registrations.Register(new AbilityRegistration("blink", "test", AbilityKind.Active));
        core.Registrations.Register(new AbilityRegistration("passive_one", "test", AbilityKind.Passive));
        core.AddPlayer(Player, "shaman", raceLevel, ("blink", 1), ("bolt", ultimateLevel));
        return core;
    }

    [Fact]
    public void SuccessfulUltimateStartsCooldownFromJson()
    {
        var core = Create(raceLevel: 6, ultimateLevel: 2);
        var calls = 0;
        core.Events.Subscribe<UltimatePressedEvent>(e =>
        {
            if (!e.IsFor("bolt"))
                return;

            calls++;
            e.Succeed();
        });

        Assert.True(core.Activation.ActivateUltimate(Player).Success);
        Assert.Equal(TimeSpan.FromSeconds(15), core.Abilities.GetCooldownRemaining(Player, "bolt"));

        var second = core.Activation.ActivateUltimate(Player);
        Assert.False(second.Success);
        Assert.Contains("перезаряжается", second.Message);
        Assert.Equal(1, calls);

        core.Time.Now += TimeSpan.FromSeconds(15);
        Assert.True(core.Activation.ActivateUltimate(Player).Success);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void FailedActivationDoesNotStartCooldown()
    {
        var core = Create(raceLevel: 6, ultimateLevel: 1);
        core.Events.Subscribe<UltimatePressedEvent>(e => e.Fail("Нет целей."));

        var result = core.Activation.ActivateUltimate(Player);

        Assert.False(result.Success);
        Assert.Equal("Нет целей.", result.Message);
        Assert.Equal(TimeSpan.Zero, core.Abilities.GetCooldownRemaining(Player, "bolt"));
    }

    [Fact]
    public void LockedUltimateIsNotPublished()
    {
        var core = Create(raceLevel: 5, ultimateLevel: 1);
        var published = false;
        core.Events.Subscribe<UltimatePressedEvent>(_ => published = true);

        var result = core.Activation.ActivateUltimate(Player);

        Assert.False(result.Success);
        Assert.False(published);
    }

    [Fact]
    public void UnhandledActivationReportsFailure()
    {
        var core = Create(raceLevel: 6, ultimateLevel: 1);
        Assert.False(core.Activation.ActivateUltimate(Player).Success);
    }

    [Fact]
    public void ActiveSlotsCountOnlyRegisteredActiveAbilities()
    {
        var core = Create(raceLevel: 6, ultimateLevel: 1);
        AbilityPressedEvent? received = null;
        core.Events.Subscribe<AbilityPressedEvent>(e =>
        {
            received = e;
            e.Succeed();
        });

        Assert.True(core.Activation.ActivateAbility(Player, 1).Success);
        Assert.Equal("blink", received?.AbilityId);
        Assert.Equal(TimeSpan.FromSeconds(10), core.Abilities.GetCooldownRemaining(Player, "blink"));
        Assert.False(core.Activation.ActivateAbility(Player, 2).Success);

        var blink = Assert.Single(core.Abilities.GetPlayerAbilities(Player), x => x.AbilityId == "blink");
        Assert.Equal(1, blink.ActiveSlot);
    }

    [Fact]
    public void ActivatableMechanicsWorkInEitherSlotButPassivesDoNot()
    {
        var core = new TestCore();
        core.LoadRaces("""
            {
              "id": "mix", "name": "Mix", "maxLevel": 10,
              "abilities": [ { "id": "bolt", "maxLevel": 1, "config": { "cooldown": 5 } } ],
              "ultimate": { "id": "blink", "maxLevel": 1, "unlockLevel": 1, "config": { "cooldown": 7 } }
            }
            """);
        core.Registrations.Register(new AbilityRegistration("bolt", "test", AbilityKind.Ultimate));
        core.Registrations.Register(new AbilityRegistration("blink", "test", AbilityKind.Active));
        core.AddPlayer(Player, "mix", 1, ("bolt", 1), ("blink", 1));
        core.Events.Subscribe<AbilityPressedEvent>(e => e.Succeed());
        core.Events.Subscribe<UltimatePressedEvent>(e => e.Succeed());

        Assert.True(core.Activation.ActivateAbility(Player, 1).Success);
        Assert.True(core.Activation.ActivateUltimate(Player).Success);
        Assert.Equal(TimeSpan.FromSeconds(7), core.Abilities.GetCooldownRemaining(Player, "blink"));

        var passiveCore = new TestCore();
        passiveCore.LoadRaces("""
            { "id": "p", "name": "P", "ultimate": { "id": "aura", "maxLevel": 1, "unlockLevel": 1 } }
            """);
        passiveCore.Registrations.Register(new AbilityRegistration("aura", "test", AbilityKind.Passive));
        passiveCore.AddPlayer(Player, "p", 1, ("aura", 1));

        Assert.False(passiveCore.Activation.ActivateUltimate(Player).Success);
    }
}
