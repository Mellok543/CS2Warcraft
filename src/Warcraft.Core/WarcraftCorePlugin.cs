using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Capabilities;
using Microsoft.Extensions.Logging;
using Warcraft.Api;
using Warcraft.Core.Abilities;
using Warcraft.Core.Events;
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

    public override void Load(bool hotReload)
    {
        var players = new PlayerStateStore();
        var races = new RaceCatalogService(players);
        var events = new WarcraftEventBus();
        var persistence = new PersistenceCoordinator();
        var progress = new ProgressionService(players, races);
        var abilities = new AbilityRegistryService(players, races);

        _api = new WarcraftApiFacade(players, progress, races, abilities, events, persistence);
        Capabilities.RegisterPluginCapability(CoreCapability, () => _api);

        Logger.LogInformation("Warcraft.Core loaded. Capability: {Capability}", WarcraftCapabilityNames.CoreApi);
    }

    public override void Unload(bool hotReload)
    {
        _api = null;
        Logger.LogInformation("Warcraft.Core unloaded.");
    }
}
