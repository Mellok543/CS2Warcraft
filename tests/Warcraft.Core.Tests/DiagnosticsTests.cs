using Warcraft.Api.Abilities;
using Warcraft.Api.Diagnostics;
using Warcraft.Api.Events;
using Warcraft.Api.Modules;
using Warcraft.Core.Diagnostics;
using Warcraft.Core.Modules;
using Warcraft.Core.Persistence;

namespace Warcraft.Core.Tests;

public sealed class DiagnosticsTests
{
    private static (TestCore Core, DiagnosticsService Diagnostics, ModuleRegistryService Modules) Create()
    {
        var core = new TestCore();
        var modules = new ModuleRegistryService();
        var diagnostics = new DiagnosticsService(
            core.Races,
            core.Registrations,
            modules,
            new PersistenceCoordinator(),
            core.Players,
            core.Events,
            core.Time);

        core.LoadRaces(
            """{ "id": "orc", "name": "Orc", "abilities": [ { "id": "critical_strike" } ], "ultimate": { "id": "chain_lightning", "unlockLevel": 1 } }""",
            """{ "id": "elf", "name": "Elf", "requirements": { "totalLevel": 5 }, "abilities": [ { "id": "missing_mechanic" } ] }""",
            """{ "id": "shade", "name": "Shade", "vipOnly": true, "ultimate": { "id": "evasion", "unlockLevel": 1 } }""");

        core.Registrations.Register(new AbilityRegistration("critical_strike", "test", AbilityKind.Passive));
        core.Registrations.Register(new AbilityRegistration("chain_lightning", "test", AbilityKind.Ultimate));
        core.Registrations.Register(new AbilityRegistration("evasion", "test", AbilityKind.Passive));

        return (core, diagnostics, modules);
    }

    [Fact]
    public void HealthClassifiesRacesAndFindsBrokenAbilityReferences()
    {
        var (_, diagnostics, _) = Create();
        using var _d = diagnostics;

        var health = diagnostics.GetRaceCatalogHealth();

        Assert.Equal(3, health.TotalRaces);
        Assert.Equal(["orc"], health.StarterRaces);
        Assert.Equal(["elf"], health.LockedRaces);
        Assert.Equal(["shade"], health.VipRaces);
        Assert.Equal(new RaceAbilityIssue("elf", "missing_mechanic", false), Assert.Single(health.UnresolvedHandlers));
        Assert.Equal(new RaceAbilityIssue("shade", "evasion", true), Assert.Single(health.PassiveUltimates));
        Assert.False(health.IsHealthy);
    }

    [Fact]
    public void StatusReportsRegistriesAndLastReloadIncludingRejectedOnes()
    {
        var (core, diagnostics, modules) = Create();
        using var _d = diagnostics;
        modules.Register(new ModuleRegistration("warcraft.core", "0.6.0"));
        core.Players.Upsert(5, "p");

        core.Events.Publish(new RaceCatalogReloadedEvent(false, 3, ["orc.json: bad"], "filesystem"));
        var status = diagnostics.GetStatus();

        Assert.Equal(3, status.RegisteredAbilities);
        Assert.Equal(3, status.LoadedRaces);
        Assert.Equal(1, status.LoadedPlayers);
        Assert.Null(status.PersistenceProvider);
        Assert.Equal("warcraft.core", Assert.Single(status.Modules).Id);
        Assert.False(status.LastRaceReload!.Success);
        Assert.Equal("filesystem", status.LastRaceReload.Source);
        Assert.Equal(["orc.json: bad"], status.LastRaceReload.Errors);
    }
}
