using System.Diagnostics.CodeAnalysis;
using Warcraft.Api.Abilities;
using Warcraft.Core.Conditions;
using Warcraft.Core.Races;
using Warcraft.Core.Runtime;

namespace Warcraft.Core.Abilities;

internal enum AbilityUsability
{
    Usable,
    PlayerNotLoaded,
    NoActiveRace,
    NotInActiveRace,
    NotLearned,
    Locked,
    ConditionsNotMet
}

/// <summary>
/// Resolves abilities of a player's active race: learned level, unlock level,
/// conditions and Core-derived values such as cooldown. No race-specific logic.
/// </summary>
internal sealed class AbilityResolver(
    PlayerStateStore players,
    RaceCatalogService races,
    AbilityRegistrationStore registrations,
    CooldownService cooldowns,
    IPlayerCombatStateProvider combatState)
{
    public PlayerAbilitySnapshot? GetSnapshot(ulong steamId, string abilityId)
    {
        if (!TryGetRace(steamId, out var player, out var race))
            return null;

        var ability = race.Find(abilityId);
        return ability is null ? null : ToSnapshot(player, race, ability);
    }

    public AbilityUsability Resolve(
        ulong steamId,
        CompiledAbility? requested,
        string abilityId,
        out PlayerAbilitySnapshot? snapshot)
    {
        snapshot = null;

        var player = players.TryGetRuntime(steamId);
        if (player is null)
            return AbilityUsability.PlayerNotLoaded;

        if (player.ActiveRaceId is null)
            return AbilityUsability.NoActiveRace;

        var race = races.GetCompiled(player.ActiveRaceId);
        if (race is null)
            return AbilityUsability.NoActiveRace;

        var ability = requested ?? race.Find(abilityId);
        if (ability is null || !ReferenceEquals(race.Find(ability.Id), ability))
            return AbilityUsability.NotInActiveRace;

        snapshot = ToSnapshot(player, race, ability);

        if (snapshot.Level <= 0)
            return AbilityUsability.NotLearned;

        var raceLevel = player.Races.GetValueOrDefault(race.Definition.Id)?.Level ?? 1;
        if (raceLevel < ability.Definition.UnlockLevel)
            return AbilityUsability.Locked;

        if (!ability.Conditions.IsEmpty)
        {
            var state = combatState.Get(steamId);
            if (state is null || !ability.Conditions.IsSatisfied(state.Value))
                return AbilityUsability.ConditionsNotMet;
        }

        return AbilityUsability.Usable;
    }

    public PlayerAbilitySnapshot? GetUsable(ulong steamId, string abilityId)
        => Resolve(steamId, null, abilityId, out var snapshot) == AbilityUsability.Usable
            ? snapshot
            : null;

    public CompiledAbility? GetUltimate(ulong steamId)
        => TryGetRace(steamId, out _, out var race) ? race.Ultimate : null;

    /// <summary>1-based slot among the race's registered <see cref="AbilityKind.Active"/> abilities.</summary>
    public CompiledAbility? GetActiveSlot(ulong steamId, int slot)
    {
        if (slot < 1 || !TryGetRace(steamId, out _, out var race))
            return null;

        return GetActiveAbilities(race).ElementAtOrDefault(slot - 1);
    }

    public IReadOnlyList<PlayerAbilityStatus> GetStatuses(ulong steamId)
    {
        if (!TryGetRace(steamId, out var player, out var race))
            return [];

        var progress = player.Races.GetValueOrDefault(race.Definition.Id);
        var raceLevel = progress?.Level ?? 1;
        var activeAbilities = GetActiveAbilities(race);
        var result = new List<PlayerAbilityStatus>(race.Abilities.Count);

        foreach (var ability in race.Abilities)
        {
            var definition = ability.Definition;
            var registration = registrations.Get(ability.Id);
            var level = progress?.AbilityLevels.GetValueOrDefault(ability.Id) ?? 0;
            var slotIndex = activeAbilities.IndexOf(ability);

            result.Add(new PlayerAbilityStatus(
                ability.Id,
                definition.Name ?? registration?.DisplayName ?? ability.Id,
                registration?.Kind ?? (ability.IsUltimate ? AbilityKind.Ultimate : AbilityKind.Passive),
                ability.IsUltimate,
                level,
                definition.MaxLevel,
                definition.UnlockLevel,
                raceLevel,
                raceLevel >= definition.UnlockLevel,
                registration is not null,
                AbilityUpgradeRules.Check(progress, definition),
                AbilityConfigValues.GetCooldown(definition.Config, level),
                cooldowns.GetRemaining(steamId, ability.Id),
                slotIndex >= 0 ? slotIndex + 1 : null,
                Describe(ability, Math.Max(1, level)),
                level < definition.MaxLevel ? Describe(ability, level + 1) : null,
                ability.Conditions.Description));
        }

        return result;
    }

    public IReadOnlyList<AbilityInfo> GetRaceInfo(string raceId)
    {
        var race = races.GetCompiled(raceId);
        if (race is null)
            return [];

        return race.Abilities
            .Select(ability =>
            {
                var registration = registrations.Get(ability.Id);
                return new AbilityInfo(
                    ability.Id,
                    GetDisplayName(ability),
                    registration?.Kind ?? (ability.IsUltimate ? AbilityKind.Ultimate : AbilityKind.Passive),
                    ability.IsUltimate,
                    ability.Definition.UnlockLevel,
                    ability.Definition.MaxLevel,
                    Describe(ability, 1),
                    Describe(ability, ability.Definition.MaxLevel),
                    ability.Conditions.Description,
                    registration is not null);
            })
            .ToArray();
    }

    /// <summary>Race JSON description overrides the handler's default template.</summary>
    private string Describe(CompiledAbility ability, int level)
    {
        var template = ability.Definition.Description ?? registrations.Get(ability.Id)?.Description;
        return string.IsNullOrWhiteSpace(template)
            ? "Описание отсутствует."
            : AbilityDescriptionFormatter.Format(template, ability.Definition.Config, level);
    }

    public string GetDisplayName(CompiledAbility ability)
        => ability.Definition.Name ?? registrations.Get(ability.Id)?.DisplayName ?? ability.Id;

    private List<CompiledAbility> GetActiveAbilities(CompiledRace race)
        => race.Abilities
            .Where(x => !x.IsUltimate && registrations.Get(x.Id)?.Kind is AbilityKind.Active or AbilityKind.Ultimate)
            .ToList();

    private bool TryGetRace(
        ulong steamId,
        [NotNullWhen(true)] out PlayerRuntimeState? player,
        [NotNullWhen(true)] out CompiledRace? race)
    {
        player = players.TryGetRuntime(steamId);
        race = player?.ActiveRaceId is null ? null : races.GetCompiled(player.ActiveRaceId);
        return player is not null && race is not null;
    }

    private static PlayerAbilitySnapshot ToSnapshot(
        PlayerRuntimeState player,
        CompiledRace race,
        CompiledAbility ability)
    {
        var definition = ability.Definition;
        var level = player.Races
            .GetValueOrDefault(race.Definition.Id)?
            .AbilityLevels.GetValueOrDefault(ability.Id) ?? 0;

        return new PlayerAbilitySnapshot(
            ability.Id,
            race.Definition.Id,
            level,
            definition.MaxLevel,
            definition.Config,
            definition.Conditions,
            definition.UnlockLevel,
            ability.IsUltimate,
            AbilityConfigValues.GetCooldown(definition.Config, level));
    }
}
