using Warcraft.Api;

namespace Warcraft.Abilities;

internal interface IAbilityHandler : IDisposable
{
    string Id { get; }
    void Register(IWarcraftApi api);
}
