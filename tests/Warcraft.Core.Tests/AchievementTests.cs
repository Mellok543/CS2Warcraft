using Warcraft.Achievements;
using Warcraft.Api;
using Warcraft.Api.Events;
using Warcraft.Core.Achievements;
using Warcraft.Core.Diagnostics;
using Warcraft.Core.Game;
using Warcraft.Core.Menu;
using Warcraft.Core.Modules;
using Warcraft.Core.Persistence;

namespace Warcraft.Core.Tests;

public sealed class AchievementTests
{
    private const ulong Player = 41;

    private static (TestCore Core, AchievementProgressService Achievements, IWarcraftApi Api) Create()
    {
        var core = new TestCore();
        core.LoadRaces("""{ "id": "orc", "name": "Orc" }""");
        core.Players.Upsert(Player, "p");

        var achievements = new AchievementProgressService(core.Players, core.Events, core.Time);
        var persistence = new PersistenceCoordinator();
        var modules = new ModuleRegistryService();

        IWarcraftApi api = new WarcraftApiFacade(
            core.Players,
            core.Progress,
            core.Races,
            core.Abilities,
            achievements,
            core.Events,
            persistence,
            core.Modifiers,
            modules,
            new MenuExtensionRegistry(),
            new AbilityDamageService(new AbilityDamagePipeline(core.Events)),
            new DiagnosticsService(core.Races, core.Registrations, modules, persistence, core.Players, core.Events, core.Time));

        return (core, achievements, api);
    }

    [Fact]
    public void ProgressIsMonotonicClampedAndUnlocksOnce()
    {
        var (core, achievements, _) = Create();
        var unlocked = new List<AchievementUnlockedEvent>();
        core.Events.Subscribe<AchievementUnlockedEvent>(unlocked.Add);

        achievements.AddProgress(Player, "a", 3, 5, "test");
        achievements.SetProgress(Player, "a", 1, 5, "test");          // never decreases
        Assert.Equal(3, achievements.GetProgress(Player, "a").Progress);

        var result = achievements.AddProgress(Player, "a", 10, 5, "test");
        achievements.AddProgress(Player, "a", 10, 5, "test");

        Assert.True(result.NewlyUnlocked);
        Assert.Equal(5, achievements.GetProgress(Player, "a").Progress);
        Assert.Single(unlocked);
        Assert.False(achievements.AddProgress(Player, "a", 0, 5, "test").Success);
        Assert.False(achievements.AddProgress(Player + 1, "a", 1, 5, "test").Success);
    }

    [Fact]
    public void ProgressIsPersistedAndRestored()
    {
        var (core, achievements, _) = Create();
        core.Players.MarkLoaded(Player);
        achievements.Unlock(Player, "combat.first_kill", "test");

        var snapshot = core.Players.GetPersistenceSnapshot(Player)!;
        var stored = Assert.Single(snapshot.Achievements);
        Assert.True(stored.Unlocked);

        core.Players.RestoreIfLoaded(snapshot, "p");
        Assert.True(achievements.GetProgress(Player, "combat.first_kill").Unlocked);
    }

    [Fact]
    public void AbilityUsesDoNotAdvanceMechanicAchievements()
    {
        var (core, achievements, api) = Create();
        using var tracker = new AchievementTracker(api, () => AchievementCatalog.Build(api));

        core.Events.Publish(new AbilityActivatedEvent(Player, "dash", false, null));

        Assert.Equal(1, achievements.GetProgress(Player, "ability.first").Progress);
        Assert.DoesNotContain(achievements.GetAll(Player), x => x.Key.StartsWith("mechanic.", StringComparison.Ordinal));
    }

    [Fact]
    public void TelemetryRulesDriveMechanicAchievements()
    {
        var (core, achievements, api) = Create();
        using var tracker = new AchievementTracker(api, () => AchievementCatalog.Build(api));

        // Max: the best single cast counts, 2 targets is not enough for "3 targets".
        core.Events.Publish(new AbilityTelemetryEvent(Player, "chain_lightning", AbilityTelemetryKind.TargetsHit, 2));
        Assert.False(achievements.GetProgress(Player, "mechanic.chain_three").Unlocked);
        core.Events.Publish(new AbilityTelemetryEvent(Player, "chain_lightning", AbilityTelemetryKind.TargetsHit, 3));
        Assert.True(achievements.GetProgress(Player, "mechanic.chain_three").Unlocked);

        // Sum across several abilities of one rule.
        core.Events.Publish(new AbilityTelemetryEvent(Player, "healing_totem", AbilityTelemetryKind.TotemPlaced));
        core.Events.Publish(new AbilityTelemetryEvent(Player, "frost_totem", AbilityTelemetryKind.TotemPlaced));
        Assert.Equal(2, achievements.GetProgress(Player, "mechanic.totems_100").Progress);

        // Kind must match: healing from vampirism does not count as reflect damage.
        core.Events.Publish(new AbilityTelemetryEvent(Player, "vampirism", AbilityTelemetryKind.Healing, 40));
        Assert.Equal(40, achievements.GetProgress(Player, "mechanic.vamp_heal_5000").Progress);
        Assert.Equal(0, achievements.GetProgress(Player, "mechanic.reflect_damage_1000").Progress);
    }

    [Fact]
    public void EveryMechanicAchievementHasATelemetryRule()
    {
        var (_, _, api) = Create();

        var mechanics = AchievementCatalog.Build(api).Where(x => x.Id.StartsWith("mechanic.", StringComparison.Ordinal)).ToArray();

        Assert.NotEmpty(mechanics);
        Assert.All(mechanics, x =>
        {
            Assert.Equal(AchievementMetric.AbilityTelemetry, x.Metric);
            Assert.NotNull(x.Telemetry);
            Assert.NotEmpty(x.Telemetry.AbilityIds);
        });
    }
}
