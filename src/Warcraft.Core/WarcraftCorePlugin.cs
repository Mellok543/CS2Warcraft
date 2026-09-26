using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Capabilities;
using Microsoft.Extensions.Logging;
using Warcraft.Api;
using Warcraft.Api.Modules;
using Warcraft.Core.Abilities;
using Warcraft.Core.Events;
using Warcraft.Core.Modifiers;
using Warcraft.Core.Modules;
using Warcraft.Core.Persistence;
using Warcraft.Core.Progression;
using Warcraft.Core.Races;
using Warcraft.Core.Runtime;

namespace Warcraft.Core;

[MinimumApiVersion(80)]
public sealed class WarcraftCorePlugin : BasePlugin
{
    public override string ModuleName => "Warcraft.Core";
    public override string ModuleVersion => "0.1.0";
    public override string ModuleAuthor => "Mellok543";
    public override string ModuleDescription =>
        "Central runtime state and API provider for CS2Warcraft.";

    public static PluginCapability<IWarcraftApi> CoreCapability { get; } =
        new(WarcraftCapabilityNames.CoreApi);

    private IWarcraftApi? _api;
    private PlayerStateStore? _players;

    public override void Load(bool hotReload)
    {
        var players = new PlayerStateStore();
        var modifiers = new ModifierService();
        var races = new RaceCatalogService(players);
        var events = new WarcraftEventBus(exception =>
            Logger.LogError(exception, "Unhandled Warcraft event subscriber exception."));
        var persistence = new PersistenceCoordinator();
        var modules = new ModuleRegistryService();
        var progress = new ProgressionService(players, races, modifiers);
        var abilities = new AbilityRegistryService(players, races);

        IWarcraftApi api = new WarcraftApiFacade(
            players,
            progress,
            races,
            abilities,
            events,
            persistence,
            modifiers,
            modules);

        _players = players;
        _api = api;

        modules.Register(new ModuleRegistration(
            "warcraft.core",
            ModuleVersion,
            "Central runtime and orchestration"));

        Capabilities.RegisterPluginCapability(CoreCapability, () => api);

        RegisterListener<Listeners.OnClientPutInServer>(OnClientPutInServer);
        RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);

        if (hotReload)
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (player is { IsValid: true, IsBot: false } && player.SteamID != 0)
                    players.Upsert(player.SteamID, player.PlayerName);
            }
        }

        Logger.LogInformation(
            "Warcraft.Core loaded. Capability: {Capability}",
            WarcraftCapabilityNames.CoreApi);
    }

    public override void Unload(bool hotReload)
    {
        _api = null;
        _players = null;
        Logger.LogInformation("Warcraft.Core unloaded.");
    }

    private void OnClientPutInServer(int playerSlot)
    {
        var player = Utilities.GetPlayerFromSlot(playerSlot);
        if (player is not { IsValid: true, IsBot: false } || player.SteamID == 0)
            return;

        _players?.Upsert(player.SteamID, player.PlayerName);
    }

    private void OnClientDisconnect(int playerSlot)
    {
        var player = Utilities.GetPlayerFromSlot(playerSlot);
        if (player is null || player.SteamID == 0)
            return;

        _players?.Remove(player.SteamID);
    }
}
