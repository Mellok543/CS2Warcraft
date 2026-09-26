using Warcraft.Api.Abilities;
using Warcraft.Core.Races;
using Warcraft.Core.Runtime;

namespace Warcraft.Core.Abilities;

internal sealed class AbilityRegistryService(
    PlayerStateStore players,
    RaceCatalogService races) : IAbilitiesApi
{
    private readonly Dictionary<string, AbilityRegistration> _registrations =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    public AbilityRegistrationResult Register(AbilityRegistration registration)
    {
        if (string.IsNullOrWhiteSpace(registration.Id))
            return new(false, "Ability id is required.");

        lock (_sync)
        {
            if (_registrations.TryGetValue(registration.Id, out var existing))
                return new(false, $"Ability '{registration.Id}' is already owned by '{existing.OwnerModule}'.");

            _registrations[registration.Id] = registration;
        }

        return new(true, $"Ability '{registration.Id}' registered.");
    }

    public bool Unregister(string abilityId, string ownerModule)
    {
        lock (_sync)
        {
            if (!_registrations.TryGetValue(abilityId, out var existing))
                return false;
            if (!string.Equals(existing.OwnerModule, ownerModule, StringComparison.OrdinalIgnoreCase))
                return false;

            return _registrations.Remove(abilityId);
        }
    }

    public IReadOnlyCollection<AbilityRegistration> GetRegistered()
    {
        lock (_sync)
            return _registrations.Values.ToArray();
    }

    public PlayerAbilitySnapshot? GetPlayerAbility(ulong steamId, string abilityId)
    {
        var player = players.GetRequired(steamId);
        if (player.ActiveRaceId is null)
            return null;

        var race = races.Get(player.ActiveRaceId);
        if (race is null)
            return null;

        var definitions = race.Abilities.Concat(race.Ultimate is null ? [] : [race.Ultimate]);
        var definition = definitions.FirstOrDefault(x =>
            string.Equals(x.Id, abilityId, StringComparison.OrdinalIgnoreCase));

        if (definition is null)
            return null;

        var progress = player.Races.GetValueOrDefault(race.Id);
        var level = progress?.AbilityLevels.GetValueOrDefault(abilityId) ?? 0;

        return new(definition.Id, race.Id, level, definition.MaxLevel, definition.Config, definition.Conditions);
    }

    public CooldownResult TryStartCooldown(ulong steamId, string abilityId, TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration));

        var player = players.GetRequired(steamId);
        var now = DateTimeOffset.UtcNow;

        if (player.Cooldowns.TryGetValue(abilityId, out var readyAt) && readyAt > now)
            return new(false, readyAt, readyAt - now);

        readyAt = now + duration;
        player.Cooldowns[abilityId] = readyAt;
        return new(true, readyAt, TimeSpan.Zero);
    }
}
