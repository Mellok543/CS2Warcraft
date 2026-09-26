namespace Warcraft.Api.Modules;

public interface IModulesApi
{
    ModuleRegistrationResult Register(ModuleRegistration registration);
    bool Unregister(string moduleId);
    IReadOnlyCollection<ModuleRegistration> GetAll();
}

public sealed record ModuleRegistration(
    string Id,
    string Version,
    string? Description = null);

public sealed record ModuleRegistrationResult(bool Success, string Message);
