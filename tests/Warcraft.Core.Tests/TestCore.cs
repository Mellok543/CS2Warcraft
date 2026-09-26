using System.Text.Json;
using Warcraft.Api.Races;
using Warcraft.Core.Abilities;
using Warcraft.Core.Conditions;
using Warcraft.Core.Events;
using Warcraft.Core.Modifiers;
using Warcraft.Core.Progression;
using Warcraft.Core.Races;
using Warcraft.Core.Runtime;

namespace Warcraft.Core.Tests;

internal sealed class ManualTimeProvider : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class FakeCombatStateProvider : IPlayerCombatStateProvider
{
    public Dictionary<ulong, PlayerCombatState> States { get; } = [];
    public PlayerCombatState? Get(ulong steamId) => States.TryGetValue(steamId, out var s) ? s : null;
}

/// <summary>Wires the CounterStrikeSharp-free part of Core exactly like the plugin does.</summary>
internal sealed class TestCore
{
    public static readonly JsonSerializerOptions RaceJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public TestCore()
    {
        Players = new PlayerStateStore(Time);
        Events = new WarcraftEventBus(exception => throw exception);
        Races = new RaceCatalogService(
            Players,
            Events,
            Modifiers,
            new RaceCatalogCompiler(AbilityConditionRegistry.CreateDefault()));
        Progress = new ProgressionService(Players, Races, Modifiers, Events);
        Cooldowns = new CooldownService(Players, Time);
        Resolver = new AbilityResolver(Players, Races, Registrations, Cooldowns, Combat);
        Abilities = new AbilitiesApiService(Registrations, Resolver, Cooldowns);
        Activation = new AbilityActivationService(Resolver, Registrations, Cooldowns, Events);
    }

    public ManualTimeProvider Time { get; } = new();
    public PlayerStateStore Players { get; }
    public ModifierService Modifiers { get; } = new();
    public FakeCombatStateProvider Combat { get; } = new();
    public AbilityRegistrationStore Registrations { get; } = new();
    public WarcraftEventBus Events { get; }
    public RaceCatalogService Races { get; }
    public ProgressionService Progress { get; }
    public CooldownService Cooldowns { get; }
    public AbilityResolver Resolver { get; }
    public AbilitiesApiService Abilities { get; }
    public AbilityActivationService Activation { get; }

    public static RaceDefinition ParseRace(string json)
        => JsonSerializer.Deserialize<RaceDefinition>(json, RaceJson)
           ?? throw new InvalidOperationException("Empty race JSON.");

    public void LoadRaces(params string[] json)
    {
        var result = Races.ReplaceCatalog(json.Select(ParseRace).ToArray(), "test");
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
    }

    /// <summary>Creates a player on <paramref name="raceId"/> with the given race level and ability levels.</summary>
    public void AddPlayer(ulong steamId, string raceId, int raceLevel, params (string Ability, int Level)[] abilities)
    {
        Players.Upsert(steamId, $"player{steamId}");
        Assert.True(Races.SelectRace(steamId, raceId, "test").Success);
        Assert.True(Progress.SetRaceLevel(steamId, raceId, raceLevel, "test").Success);

        foreach (var (ability, level) in abilities)
            Assert.True(Progress.SetAbilityLevel(steamId, ability, level, "test").Success);

        Combat.States[steamId] = new PlayerCombatState(true, 100, 100, true, "weapon_ak47", WeaponCategories.Rifle);
    }
}
