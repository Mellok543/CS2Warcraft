using Warcraft.Api.Abilities;
using Warcraft.Api.Events;
using Warcraft.Api.Modifiers;
using Warcraft.Api.Modules;
using Warcraft.Api.Persistence;
using Warcraft.Api.Players;
using Warcraft.Api.Progression;
using Warcraft.Api.Races;

namespace Warcraft.Api;

public interface IWarcraftApi
{
    IPlayersApi Players { get; }
    IProgressApi Progress { get; }
    IRacesApi Races { get; }
    IAbilitiesApi Abilities { get; }
    IWarcraftEventBus Events { get; }
    IPersistenceApi Persistence { get; }
    IModifiersApi Modifiers { get; }
    IModulesApi Modules { get; }
}
