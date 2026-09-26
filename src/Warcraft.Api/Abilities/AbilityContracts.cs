using System.Text.Json;

namespace Warcraft.Api.Abilities;

public interface IAbilitiesApi
{
    AbilityRegistrationResult Register(AbilityRegistration registration);
    bool Unregister(string abilityId, string ownerModule);
    IReadOnlyCollection<AbilityRegistration> GetRegistered();

    /// <summary>
    /// Returns the raw ability definition of the player's active race merged with
    /// the player's learned level. Does not check unlock level or conditions.
    /// </summary>
    PlayerAbilitySnapshot? GetPlayerAbility(ulong steamId, string abilityId);

    /// <summary>
    /// Returns the ability only when it can take effect right now: the player has
    /// learned it, the race level satisfies unlockLevel and all JSON conditions pass.
    /// Must be called on the game thread.
    /// </summary>
    PlayerAbilitySnapshot? GetUsableAbility(ulong steamId, string abilityId);

    /// <summary>
    /// Core-computed view of every ability of the player's active race.
    /// UI modules render this instead of re-implementing Core rules.
    /// </summary>
    IReadOnlyList<PlayerAbilityStatus> GetPlayerAbilities(ulong steamId);

    /// <summary>Descriptions of a race's abilities for previews (e.g. before selecting the race).</summary>
    IReadOnlyList<AbilityInfo> GetRaceAbilities(string raceId);

    TimeSpan GetCooldownRemaining(ulong steamId, string abilityId);
    CooldownResult TryStartCooldown(ulong steamId, string abilityId, TimeSpan duration);
}

/// <param name="Description">
/// Default description template shown to players, e.g. <c>"Шанс {chance%} нанести x{damageMultiplier} урона."</c>
/// Races may override it with their own <c>description</c>.
/// </param>
public sealed record AbilityRegistration(
    string Id,
    string OwnerModule,
    AbilityKind Kind,
    string? Description = null,
    string? DisplayName = null);

public enum AbilityKind
{
    Passive,
    Active,
    Ultimate
}

public sealed record AbilityRegistrationResult(bool Success, string Message);

public sealed record PlayerAbilitySnapshot(
    string AbilityId,
    string RaceId,
    int Level,
    int MaxLevel,
    JsonElement Config,
    JsonElement Conditions,
    int UnlockLevel = 1,
    bool IsUltimate = false,
    TimeSpan Cooldown = default);

public sealed record PlayerAbilityStatus(
    string AbilityId,
    string DisplayName,
    AbilityKind Kind,
    bool IsUltimate,
    int Level,
    int MaxLevel,
    int UnlockLevel,
    int RaceLevel,
    bool IsUnlocked,
    bool HandlerRegistered,
    AbilityUpgradeBlock UpgradeBlock,
    TimeSpan Cooldown,
    TimeSpan CooldownRemaining,
    int? ActiveSlot,
    string Description,
    string? NextLevelDescription,
    string? ConditionsDescription)
{
    public bool IsLearned => Level > 0;
    public bool CanUpgrade => UpgradeBlock == AbilityUpgradeBlock.None;
}

public sealed record AbilityInfo(
    string AbilityId,
    string DisplayName,
    AbilityKind Kind,
    bool IsUltimate,
    int UnlockLevel,
    int MaxLevel,
    string FirstLevelDescription,
    string MaxLevelDescription,
    string? ConditionsDescription,
    bool HandlerRegistered);

public enum AbilityUpgradeBlock
{
    None,
    NoActiveRace,
    NotInActiveRace,
    RaceLevelTooLow,
    MaxLevelReached,
    NoSkillPoints
}

public sealed record CooldownResult(
    bool Success,
    DateTimeOffset? ReadyAt,
    TimeSpan Remaining);
