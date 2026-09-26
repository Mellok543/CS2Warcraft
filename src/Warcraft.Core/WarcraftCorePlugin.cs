using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Capabilities;
using Microsoft.Extensions.Logging;
using Warcraft.Api;
using Warcraft.Api.Events;
using Warcraft.Api.Modules;
using Warcraft.Api.Persistence;
using Warcraft.Core.Abilities;
using Warcraft.Core.Conditions;
using Warcraft.Core.Events;
using Warcraft.Core.Game;
using Warcraft.Core.Menu;
using Warcraft.Core.Modifiers;
using Warcraft.Core.Modules;
using Warcraft.Core.Persistence;
using Warcraft.Core.Progression;
using Warcraft.Core.Races;
using Warcraft.Core.Runtime;
using Warcraft.Core.Stats;

namespace Warcraft.Core;

[MinimumApiVersion(80)]
public sealed class WarcraftCorePlugin : BasePlugin
{
    public override string ModuleName => "Warcraft.Core";
    public override string ModuleVersion => WarcraftVersion.Current;
    public override string ModuleAuthor => "Mellok543";
    public override string ModuleDescription =>
        "Central runtime state, event bridge and API provider for CS2Warcraft.";

    public static PluginCapability<IWarcraftApi> CoreCapability { get; } =
        new(WarcraftCapabilityNames.CoreApi);

    private IWarcraftApi? _api;
    private PlayerStateStore? _players;
    private PersistenceCoordinator? _persistence;
    private CancellationTokenSource? _lifetime;
    private PersistenceSaveScheduler? _saveScheduler;
    private IDisposable? _stateChangedSubscription;
    private XpRewardService? _xpRewards;
    private readonly AbilityDamageService _abilityDamage = new();
    private PlayerNotifier? _notifier;
    private StatsService? _stats;
    private readonly IGameThreadDispatcher _gameThread = new CssGameThreadDispatcher();
    private CoreConfig _config = new();
    private CoreGameEventBridge? _eventBridge;
    private PlayerLifecycleCoordinator? _playerLifecycle;

    public override void Load(bool hotReload)
    {
        _config = CoreConfig.LoadOrCreate();

        var players = new PlayerStateStore();
        var modifiers = new ModifierService();
        var events = new WarcraftEventBus(exception =>
            Logger.LogError(exception, "Unhandled Warcraft event subscriber exception."));
        var races = new RaceCatalogService(
            players,
            events,
            modifiers,
            new RaceCatalogCompiler(AbilityConditionRegistry.CreateDefault()));
        var persistence = new PersistenceCoordinator();
        var modules = new ModuleRegistryService();
        var menu = new MenuExtensionRegistry();
        var progress = new ProgressionService(players, races, modifiers, events);
        var registrations = new AbilityRegistrationStore();
        var cooldowns = new CooldownService(players, TimeProvider.System);
        var resolver = new AbilityResolver(
            players,
            races,
            registrations,
            cooldowns,
            new CssPlayerCombatStateProvider());
        var abilities = new AbilitiesApiService(registrations, resolver, cooldowns);
        var activation = new AbilityActivationService(resolver, registrations, cooldowns, events);

        IWarcraftApi api = new WarcraftApiFacade(
            players,
            progress,
            races,
            abilities,
            events,
            persistence,
            modifiers,
            modules,
            menu,
            _abilityDamage);

        _players = players;
        _persistence = persistence;
        _api = api;
        var eventBridge = new CoreGameEventBridge(() => _api, _abilityDamage, _config);
        _eventBridge = eventBridge;
        _lifetime = new CancellationTokenSource();
        _saveScheduler = new PersistenceSaveScheduler(
            players,
            persistence,
            _gameThread,
            TimeSpan.FromMilliseconds(Math.Max(100, _config.AutosaveDelayMilliseconds)),
            Logger);

        var playerLifecycle = new PlayerLifecycleCoordinator(
            players,
            persistence,
            _saveScheduler,
            _gameThread,
            Logger,
            _lifetime.Token);
        playerLifecycle.Start();
        _playerLifecycle = playerLifecycle;

        _stats = new StatsService(players, events);

        _stateChangedSubscription = events.Subscribe<PlayerStateChangedEvent>(
            changed => _saveScheduler?.Schedule(changed.SteamId));

        _xpRewards = new XpRewardService(players, progress, events, _config);
        _notifier = new PlayerNotifier(events);


        modules.Register(new ModuleRegistration(
            "warcraft.core",
            ModuleVersion,
            "Central runtime, event bridge and orchestration"));

        Capabilities.RegisterPluginCapability(CoreCapability, () => api);

        RegisterListener<Listeners.OnClientPutInServer>(playerLifecycle.OnClientPutInServer);
        RegisterListener<Listeners.OnClientDisconnect>(playerLifecycle.OnClientDisconnect);
        RegisterListener<Listeners.OnPlayerTakeDamagePre>(eventBridge.OnPlayerTakeDamagePre);
        RegisterListener<Listeners.OnPlayerTakeDamagePost>(eventBridge.OnPlayerTakeDamagePost);
        RegisterListener<Listeners.OnTick>(eventBridge.OnTick);

        new AbilityInputCommands(activation).Register(this);

        RegisterEventHandler<EventPlayerSpawn>(eventBridge.OnPlayerSpawn);
        RegisterEventHandler<EventPlayerJump>(eventBridge.OnPlayerJump);
        RegisterEventHandler<EventPlayerHurt>(eventBridge.OnPlayerHurt);
        RegisterEventHandler<EventPlayerDeath>(eventBridge.OnPlayerDeathPre, HookMode.Pre);
        RegisterEventHandler<EventPlayerDeath>(eventBridge.OnPlayerDeath);
        RegisterEventHandler<EventWeaponFire>(eventBridge.OnWeaponFire);
        RegisterEventHandler<EventRoundStart>(eventBridge.OnRoundStart);
        RegisterEventHandler<EventRoundEnd>(eventBridge.OnRoundEnd);
        RegisterEventHandler<EventBombPlanted>(eventBridge.OnBombPlanted);
        RegisterEventHandler<EventBombDefused>(eventBridge.OnBombDefused);

        if (hotReload)
            playerLifecycle.AdoptConnectedPlayers();

        Logger.LogInformation(
            "Warcraft.Core loaded. Capability: {Capability}",
            WarcraftCapabilityNames.CoreApi);
    }

    public override void Unload(bool hotReload)
    {
        _playerLifecycle?.Dispose();
        _playerLifecycle = null;

        _lifetime?.Cancel();

        _stateChangedSubscription?.Dispose();
        _stateChangedSubscription = null;

        _xpRewards?.Dispose();
        _xpRewards = null;

        _notifier?.Dispose();
        _notifier = null;

        _stats?.Dispose();
        _stats = null;

        _saveScheduler?.Dispose();
        _saveScheduler = null;

        if (_players is not null && _persistence is { HasProvider: true })
        {
            foreach (var snapshot in _players.GetPersistenceSnapshots())
            {
                try
                {
                    _persistence.SavePlayerAsync(snapshot)
                        .AsTask()
                        .GetAwaiter()
                        .GetResult();
                }
                catch (Exception exception)
                {
                    Logger.LogError(
                        exception,
                        "Failed to save Warcraft state for {SteamId} during unload.",
                        snapshot.SteamId);
                }
            }
        }

        _lifetime?.Dispose();
        _lifetime = null;
        _eventBridge = null;
        _api = null;
        _players = null;
        _persistence = null;

        Logger.LogInformation("Warcraft.Core unloaded.");
    }

}
