using Warcraft.Api;
using Warcraft.Api.Abilities;
using Warcraft.Api.Events;
using Warcraft.Api.Persistence;
using Warcraft.Api.Players;
using Warcraft.Api.Progression;
using Warcraft.Api.Races;

namespace Warcraft.Core;

internal sealed class WarcraftApiFacade(
    IPlayersApi players,
    IProgressApi progress,
    IRacesApi races,
    IAbilitiesApi abilities,
    IWarcraftEventBus events,
    IPersistenceApi persistence) : IWarcraftApi
{
    public IPlayersApi Players { get; } = players;
    public IProgressApi Progress { get; } = progress;
    public IRacesApi Races { get; } = races;
    public IAbilitiesApi Abilities { get; } = abilities;
    public IWarcraftEventBus Events { get; } = events;
    public IPersistenceApi Persistence { get; } = persistence;
}
