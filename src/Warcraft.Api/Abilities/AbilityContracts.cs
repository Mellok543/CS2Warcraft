using System.Text.Json;

namespace Warcraft.Api.Abilities;

public interface IAbilitiesApi
{
    AbilityRegistrationResult Register(AbilityRegistration registration);
    bool Unregister(string abilityId, string ownerModule);
    IReadOnlyCollection<AbilityRegistration> GetRegistered();
    PlayerAbilitySnapshot? GetPlayerAbility(ulong steamId, string abilityId);
    CooldownResult TryStartCooldown(ulong steamId, string abilityId, TimeSpan duration);
}

public sealed record AbilityRegistration(
    string Id,
    string OwnerModule,
    AbilityKind Kind,
    string? Description = null);

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
    JsonElement Conditions);

public sealed record CooldownResult(
    bool Success,
    DateTimeOffset? ReadyAt,
    TimeSpan Remaining);
