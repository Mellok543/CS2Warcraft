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

namespace Warcraft.Api;

public interface IWarcraftApi
{
    IPlayersApi Players { get; }
    IProgressApi Progress { get; }
    IRacesApi Races { get; }
    IAbilitiesApi Abilities { get; }
    IAchievementsApi Achievements { get; }
    ICosmeticsApi Cosmetics { get; }
    IWarcraftEventBus Events { get; }
    IPersistenceApi Persistence { get; }
    IModifiersApi Modifiers { get; }
    IModulesApi Modules { get; }
    IMenuExtensionsApi Menu { get; }
    ICombatApi Combat { get; }
    IDiagnosticsApi Diagnostics { get; }
}
