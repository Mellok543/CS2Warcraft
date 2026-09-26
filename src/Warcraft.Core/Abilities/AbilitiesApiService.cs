using Warcraft.Api.Abilities;

namespace Warcraft.Core.Abilities;

internal sealed class AbilitiesApiService(
    AbilityRegistrationStore registrations,
    AbilityResolver resolver,
    CooldownService cooldowns) : IAbilitiesApi
{
    public AbilityRegistrationResult Register(AbilityRegistration registration)
        => registrations.Register(registration);

    public bool Unregister(string abilityId, string ownerModule)
        => registrations.Unregister(abilityId, ownerModule);

    public IReadOnlyCollection<AbilityRegistration> GetRegistered()
        => registrations.GetAll();

    public PlayerAbilitySnapshot? GetPlayerAbility(ulong steamId, string abilityId)
        => resolver.GetSnapshot(steamId, abilityId);

    public PlayerAbilitySnapshot? GetUsableAbility(ulong steamId, string abilityId)
        => resolver.GetUsable(steamId, abilityId);

    public IReadOnlyList<PlayerAbilityStatus> GetPlayerAbilities(ulong steamId)
        => resolver.GetStatuses(steamId);

    public IReadOnlyList<AbilityInfo> GetRaceAbilities(string raceId)
        => resolver.GetRaceInfo(raceId);

    public TimeSpan GetCooldownRemaining(ulong steamId, string abilityId)
        => cooldowns.GetRemaining(steamId, abilityId);

    public CooldownResult TryStartCooldown(ulong steamId, string abilityId, TimeSpan duration)
        => cooldowns.TryStart(steamId, abilityId, duration);
}
