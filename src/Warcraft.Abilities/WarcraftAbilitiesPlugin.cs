using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Capabilities;
using Microsoft.Extensions.Logging;
using Warcraft.Abilities.Game;
using Warcraft.Api;
using Warcraft.Api.Modules;

namespace Warcraft.Abilities;

[MinimumApiVersion(80)]
public sealed class WarcraftAbilitiesPlugin : BasePlugin
{
    public override string ModuleName => "Warcraft.Abilities";
    public override string ModuleVersion => "0.1.0";
    public override string ModuleAuthor => "Mellok543";
    public override string ModuleDescription =>
        "Reusable event-driven ability handlers for CS2Warcraft.";

    private static PluginCapability<IWarcraftApi> CoreCapability { get; } =
        new(WarcraftCapabilityNames.CoreApi);

    private IWarcraftApi? _api;
    private readonly List<IAbilityHandler> _handlers = [];

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        _api = CoreCapability.Get();
        if (_api is null)
        {
            Logger.LogError(
                "Warcraft.Core capability '{Capability}' is unavailable.",
                WarcraftCapabilityNames.CoreApi);
            return;
        }

        _api.Modules.Register(new ModuleRegistration(
            "warcraft.abilities",
            ModuleVersion,
            "Reusable ability handlers"));

        var beams = new BeamEffects(new PluginGameScheduler(this));

        Register(new CriticalStrikeAbility());
        Register(new BonusHealthAbility());
        Register(new VampirismAbility());
        Register(new ChainLightningAbility(beams));
        Register(new SpeedAbility());
        Register(new LowGravityAbility());
        Register(new RegenerationAbility());
        Register(new EvasionAbility());
        Register(new ReflectDamageAbility());

        Logger.LogInformation(
            "Warcraft.Abilities registered {Count} handlers.",
            _handlers.Count);
    }

    public override void Unload(bool hotReload)
    {
        for (var i = _handlers.Count - 1; i >= 0; i--)
            _handlers[i].Dispose();

        _handlers.Clear();

        _api?.Modules.Unregister("warcraft.abilities");
        _api = null;
    }

    private void Register(IAbilityHandler handler)
    {
        if (_api is null)
            throw new InvalidOperationException("Warcraft.Core API is unavailable.");

        try
        {
            handler.Register(_api);
            _handlers.Add(handler);
        }
        catch (InvalidOperationException exception)
        {
            Logger.LogError(exception, "Failed to register ability handler {AbilityId}.", handler.Id);
        }
    }
}
