using Warcraft.Api;
using Warcraft.Api.Abilities;
using Warcraft.Api.Combat;
using Warcraft.Api.Events;
using Warcraft.Api.Menu;
using Warcraft.Api.Modifiers;
using Warcraft.Api.Modules;
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
    IPersistenceApi persistence,
    IModifiersApi modifiers,
    IModulesApi modules,
    IMenuExtensionsApi menu,
    ICombatApi combat) : IWarcraftApi
{
    public IPlayersApi Players { get; } = players;
    public IProgressApi Progress { get; } = progress;
    public IRacesApi Races { get; } = races;
    public IAbilitiesApi Abilities { get; } = abilities;
    public IWarcraftEventBus Events { get; } = events;
    public IPersistenceApi Persistence { get; } = persistence;
    public IModifiersApi Modifiers { get; } = modifiers;
    public IModulesApi Modules { get; } = modules;
    public IMenuExtensionsApi Menu { get; } = menu;
    public ICombatApi Combat { get; } = combat;
}
