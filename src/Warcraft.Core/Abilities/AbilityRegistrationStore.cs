using Warcraft.Api.Abilities;

namespace Warcraft.Core.Abilities;

/// <summary>Thread-safe registry of ability handlers announced by feature modules.</summary>
internal sealed class AbilityRegistrationStore
{
    private readonly Dictionary<string, AbilityRegistration> _registrations =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    public AbilityRegistrationResult Register(AbilityRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        if (string.IsNullOrWhiteSpace(registration.Id))
            return new(false, "Ability id is required.");

        if (string.IsNullOrWhiteSpace(registration.OwnerModule))
            return new(false, "Ability owner module is required.");

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

    public AbilityRegistration? Get(string abilityId)
    {
        lock (_sync)
            return _registrations.GetValueOrDefault(abilityId);
    }

    public IReadOnlyCollection<AbilityRegistration> GetAll()
    {
        lock (_sync)
            return _registrations.Values.ToArray();
    }
}
