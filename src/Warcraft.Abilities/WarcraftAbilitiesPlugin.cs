using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using Microsoft.Extensions.Logging;
using Warcraft.Abilities.Actives;
using Warcraft.Abilities.Game;
using Warcraft.Abilities.Passives;
using Warcraft.Api;
using Warcraft.Api.Events;
using Warcraft.Shared;
using Warcraft.Api.Modules;

namespace Warcraft.Abilities;

[MinimumApiVersion(80)]
public sealed class WarcraftAbilitiesPlugin : BasePlugin
{
    public override string ModuleName => "Warcraft.Abilities";
    public override string ModuleVersion => WarcraftVersion.Current;
    public override string ModuleAuthor => "Mellok543";
    public override string ModuleDescription =>
        "Reusable event-driven ability handlers for CS2Warcraft.";

    private IWarcraftApi? _api;
    private readonly List<IAbilityHandler> _handlers = [];
    private readonly List<IDisposable> _systems = [];
    private BeamEffects? _beams;
    private PropEffects? _props;
    private VisualsConfig _visuals = new();
    private MovementController? _movement;

    public override void Load(bool hotReload)
    {
        _visuals = VisualsConfig.LoadOrCreate();

        // Models must be precached on map load; the listener is registered before any map starts.
        RegisterListener<Listeners.OnServerPrecacheResources>(manifest =>
        {
            if (!_visuals.Models)
                return;

            foreach (var model in WarcraftModels.All)
                manifest.AddResource(model);
        });
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        _api = WarcraftCoreCapability.TryGet();
        if (_api is null)
        {
            Logger.LogError(
                WarcraftCoreCapability.UnavailableMessage,
                WarcraftCapabilityNames.CoreApi);
            return;
        }

        _api.Modules.Register(new ModuleRegistration(
            "warcraft.abilities",
            ModuleVersion,
            "Reusable ability handlers"));

        var scheduler = new PluginGameScheduler(this);
        var beams = new BeamEffects(scheduler);
        var movement = new MovementController();
        _beams = beams;
        var props = new PropEffects(scheduler, _visuals.Models);
        _props = props;
        _movement = movement;
        var dots = new DamageOverTime(_api);
        var history = new PositionHistory();
        var buffs = new TeamBuffs();
        var totems = new TotemSystem(beams, props);

        // Buff modifiers must run before ability handlers (cheat_death needs the final damage).
        _systems.AddRange(buffs.Attach(_api.Events, () => Server.CurrentTime));

        // Passives
        Register(new CriticalStrikeAbility());
        Register(new BonusHealthAbility());
        Register(new VampirismAbility());
        Register(new SpeedAbility(movement));
        Register(new LowGravityAbility());
        Register(new RegenerationAbility());
        Register(new EvasionAbility());
        Register(new ReflectDamageAbility());
        Register(new BashAbility(movement));
        Register(new InvisibilityAbility());
        Register(new DamageReductionAbility());
        Register(new BonusDamageAbility());
        Register(new FallImmunityAbility());
        Register(new PoisonAbility(dots));
        Register(new KillHealAbility());
        Register(new PlunderAbility());
        Register(new ReincarnationAbility(scheduler));
        Register(new SpawnArmorAbility(scheduler));
        Register(new SpawnItemsAbility(scheduler));
        Register(new HeadshotDamageAbility());
        Register(new BackstabAbility());
        Register(new ExecuteAbility());
        Register(new BlastResistAbility());
        Register(new MoneyStealAbility());
        Register(new SpawnMoneyAbility());
        Register(new AdrenalineAbility(movement));
        Register(new KillSpeedAbility(movement));
        Register(new JumpBoostAbility());
        Register(new HealAuraAbility() { Props = props });
        Register(new ImmolationAbility() { Props = props });
        Register(new SlowAuraAbility(movement) { Props = props });
        Register(new SpeedAuraAbility(movement) { Props = props });
        Register(new CommandAuraAbility(buffs) { Props = props });
        Register(new DevotionAuraAbility(buffs) { Props = props });
        Register(new VampiricAuraAbility(buffs) { Props = props });
        Register(new SecondWindAbility());

        // Activatable (ability slot or ultimate)
        Register(new ChainLightningAbility(beams));
        Register(new DashAbility());
        Register(new HealBurstAbility());
        Register(new DivineShieldAbility(props));
        Register(new SprintAbility(movement));
        Register(new WarStompAbility(movement));
        Register(new EntangleAbility(movement, dots, beams, props));
        Register(new LifeDrainAbility(beams));
        Register(new RecallAbility(history));
        Register(new SwapAbility());
        Register(new PullAbility());
        Register(new RepulseAbility());
        Register(new CloakAbility());
        Register(new BattleCryAbility());
        Register(new ResurrectAbility());
        Register(new HealingTotemAbility(totems));
        Register(new FlameTotemAbility(totems));
        Register(new FrostTotemAbility(totems, movement));
        Register(new WarTotemAbility(totems, buffs));
        Register(new ShieldTotemAbility(totems, buffs));
        Register(new TurretTotemAbility(totems, beams));
        Register(new SmiteAbility(beams));
        Register(new RageAbility(buffs, movement));

        // Must be the last DamagePreEvent subscriber: it needs the final damage.
        Register(new CheatDeathAbility());

        // Shared systems run after the handlers have updated their state for this tick.
        _systems.Add(_api.Events.Subscribe<GameTickEvent>(tick =>
        {
            movement.Update(tick.ServerTime);
            dots.Update(tick.ServerTime);
            history.Record(tick.ServerTime);
            totems.Update(tick.ServerTime);
        }));
        _systems.Add(_api.Events.Subscribe<RoundStartEvent>(_ =>
        {
            movement.Clear();
            dots.Clear();
            history.Clear();
            buffs.Clear();
            totems.Clear();
            beams.PruneInvalid();
            props.PruneInvalid();
        }));

        Logger.LogInformation(
            "Warcraft.Abilities registered {Count} handlers.",
            _handlers.Count);
    }

    public override void Unload(bool hotReload)
    {
        foreach (var system in _systems)
            system.Dispose();

        _systems.Clear();

        for (var i = _handlers.Count - 1; i >= 0; i--)
            _handlers[i].Dispose();

        _handlers.Clear();

        // Unload runs on the game thread: restore entities touched by shared systems.
        _movement?.ResetAll();
        _movement = null;
        _beams?.Dispose();
        _beams = null;
        _props?.Dispose();
        _props = null;

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
