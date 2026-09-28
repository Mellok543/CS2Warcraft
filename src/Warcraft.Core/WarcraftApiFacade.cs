using Warcraft.Api;
using Warcraft.Api.Abilities;
using Warcraft.Api.Achievements;
using Warcraft.Api.Combat;
using Warcraft.Api.Cosmetics;
using Warcraft.Api.Diagnostics;
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
    IAchievementsApi achievements,
    ICosmeticsApi cosmetics,
    IWarcraftEventBus events,
    IPersistenceApi persistence,
    IModifiersApi modifiers,
    IModulesApi modules,
    IMenuExtensionsApi menu,
    ICombatApi combat,
    IDiagnosticsApi diagnostics) : IWarcraftApi
{
    public IPlayersApi Players { get; } = players;
    public IProgressApi Progress { get; } = progress;
    public IRacesApi Races { get; } = races;
    public IAbilitiesApi Abilities { get; } = abilities;
    public IAchievementsApi Achievements { get; } = achievements;
    public ICosmeticsApi Cosmetics { get; } = cosmetics;
    public IWarcraftEventBus Events { get; } = events;
    public IPersistenceApi Persistence { get; } = persistence;
    public IModifiersApi Modifiers { get; } = modifiers;
    public IModulesApi Modules { get; } = modules;
    public IMenuExtensionsApi Menu { get; } = menu;
    public ICombatApi Combat { get; } = combat;
    public IDiagnosticsApi Diagnostics { get; } = diagnostics;
}
