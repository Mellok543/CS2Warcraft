using Warcraft.Api.Modules;

namespace Warcraft.Core.Modules;

internal sealed class ModuleRegistryService : IModulesApi
{
    private readonly Dictionary<string, ModuleRegistration> _modules =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    public ModuleRegistrationResult Register(ModuleRegistration registration)
    {
        if (string.IsNullOrWhiteSpace(registration.Id))
            return new(false, "Module id is required.");

        lock (_sync)
        {
            if (!_modules.TryAdd(registration.Id, registration))
                return new(false, $"Module '{registration.Id}' is already registered.");
        }

        return new(true, $"Module '{registration.Id}' registered.");
    }

    public bool Unregister(string moduleId)
    {
        lock (_sync)
            return _modules.Remove(moduleId);
    }

    public IReadOnlyCollection<ModuleRegistration> GetAll()
    {
        lock (_sync)
            return _modules.Values.ToArray();
    }
}
