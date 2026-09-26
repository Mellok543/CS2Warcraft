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
    public override string ModuleVersion => "0.3.0";
    public override string ModuleAuthor => "Mellok543";
    public override string ModuleDescription =>
        "Central runtime state, event bridge and API provider for CS2Warcraft.";

    public static PluginCapability<IWarcraftApi> CoreCapability { get; } =
        new(WarcraftCapabilityNames.CoreApi);

    private IWarcraftApi? _api;
    private PlayerStateStore? _players;
    private PersistenceCoordinator? _persistence;
    private CancellationTokenSource? _lifetime;

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
        _persistence = persistence;
        _api = api;
        _lifetime = new CancellationTokenSource();

        persistence.FirstProviderRegistered += OnFirstPersistenceProviderRegistered;

        modules.Register(new ModuleRegistration(
            "warcraft.core",
            ModuleVersion,
            "Central runtime, event bridge and orchestration"));

        Capabilities.RegisterPluginCapability(CoreCapability, () => api);

        RegisterListener<Listeners.OnClientPutInServer>(OnClientPutInServer);
        RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
        RegisterListener<Listeners.OnPlayerTakeDamagePre>(OnPlayerTakeDamagePre);
        RegisterListener<Listeners.OnPlayerTakeDamagePost>(OnPlayerTakeDamagePost);

        RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
        RegisterEventHandler<EventPlayerHurt>(OnPlayerHurt);
        RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
        RegisterEventHandler<EventWeaponFire>(OnWeaponFire);
        RegisterEventHandler<EventRoundStart>(OnRoundStart);
        RegisterEventHandler<EventRoundEnd>(OnRoundEnd);

        if (hotReload)
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (IsHuman(player))
                    players.Upsert(player.SteamID, player.PlayerName);
            }
        }

        Logger.LogInformation(
            "Warcraft.Core loaded. Capability: {Capability}",
            WarcraftCapabilityNames.CoreApi);
    }

    public override void Unload(bool hotReload)
    {
        if (_persistence is not null)
            _persistence.FirstProviderRegistered -= OnFirstPersistenceProviderRegistered;

        _lifetime?.Cancel();

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
        _api = null;
        _players = null;
        _persistence = null;

        Logger.LogInformation("Warcraft.Core unloaded.");
    }

    private void OnClientPutInServer(int playerSlot)
    {
        var player = Utilities.GetPlayerFromSlot(playerSlot);
        if (!IsHuman(player))
            return;

        var human = player!;
        _players?.Upsert(human.SteamID, human.PlayerName);

        if (_persistence is { HasProvider: true })
            _ = LoadPlayerAsync(human.SteamID, human.PlayerName);
    }

    private void OnClientDisconnect(int playerSlot)
    {
        var player = Utilities.GetPlayerFromSlot(playerSlot);
        if (player is null || player.SteamID == 0 || _players is null)
            return;

        var steamId = player.SteamID;
        var snapshot = _players.GetPersistenceSnapshot(steamId);
        _players.Remove(steamId);

        if (snapshot is not null && _persistence is { HasProvider: true })
            _ = SavePlayerAsync(snapshot);
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (IsHuman(player))
            _api?.Events.Publish(new PlayerSpawnEvent(player!.SteamID));

        return HookResult.Continue;
    }

    private HookResult OnPlayerHurt(EventPlayerHurt @event, GameEventInfo info)
    {
        var victim = @event.Userid;
        if (!IsHuman(victim))
            return HookResult.Continue;

        var humanVictim = victim!;
        var attacker = @event.Attacker;
        ulong? attackerSteamId = IsHuman(attacker) ? attacker!.SteamID : null;

        _api?.Events.Publish(new PlayerHurtEvent(
            humanVictim.SteamID,
            attackerSteamId,
            @event.DmgHealth));

        return HookResult.Continue;
    }

    private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        var victim = @event.Userid;
        if (!IsHuman(victim))
            return HookResult.Continue;

        var humanVictim = victim!;
        var attacker = @event.Attacker;
        ulong? killerSteamId = IsHuman(attacker) ? attacker!.SteamID : null;

        _api?.Events.Publish(new PlayerDeathEvent(
            humanVictim.SteamID,
            killerSteamId));

        if (killerSteamId.HasValue && killerSteamId.Value != humanVictim.SteamID)
        {
            _api?.Events.Publish(new PlayerKillEvent(
                killerSteamId.Value,
                humanVictim.SteamID,
                @event.Headshot));
        }

        return HookResult.Continue;
    }

    private HookResult OnWeaponFire(EventWeaponFire @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (IsHuman(player))
        {
            _api?.Events.Publish(new WeaponFireEvent(
                player!.SteamID,
                @event.Weapon));
        }

        return HookResult.Continue;
    }

    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _api?.Events.Publish(new RoundStartEvent());
        return HookResult.Continue;
    }

    private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        _api?.Events.Publish(new RoundEndEvent(@event.Winner));
        return HookResult.Continue;
    }

    private HookResult OnPlayerTakeDamagePre(
        CCSPlayerPawn victimPawn,
        CTakeDamageInfo damageInfo)
    {
        var victimController = victimPawn.OriginalController.Value;
        if (!IsHuman(victimController))
            return HookResult.Continue;

        var attackerSteamId = GetAttackerSteamId(damageInfo);
        var damageEvent = new DamagePreEvent
        {
            VictimSteamId = victimController!.SteamID,
            AttackerSteamId = attackerSteamId,
            Damage = damageInfo.Damage,
            Weapon = GetAttackerWeapon(damageInfo)
        };

        _api?.Events.Publish(damageEvent);
        damageInfo.Damage = Math.Max(0.0f, damageEvent.Damage);

        return HookResult.Continue;
    }

    private void OnPlayerTakeDamagePost(
        CCSPlayerPawn victimPawn,
        CTakeDamageInfo damageInfo,
        CTakeDamageResult result)
    {
        var victimController = victimPawn.OriginalController.Value;
        if (!IsHuman(victimController))
            return;

        _api?.Events.Publish(new DamagePostEvent(
            victimController!.SteamID,
            GetAttackerSteamId(damageInfo),
            result.DamageDealt,
            GetAttackerWeapon(damageInfo)));
    }

    private void OnFirstPersistenceProviderRegistered()
    {
        if (_players is null)
            return;

        foreach (var player in _players.GetLoadedPlayers())
            _ = LoadPlayerAsync(player.SteamId, player.Name);
    }

    private async Task LoadPlayerAsync(ulong steamId, string currentName)
    {
        if (_persistence is not { HasProvider: true } ||
            _players is null ||
            _lifetime is null)
        {
            return;
        }

        try
        {
            var persisted = await _persistence.LoadPlayerAsync(
                steamId,
                _lifetime.Token);

            if (persisted is not null)
                _players.RestoreIfLoaded(persisted, currentName);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Logger.LogError(
                exception,
                "Failed to load Warcraft state for {SteamId}.",
                steamId);
        }
    }

    private async Task SavePlayerAsync(PlayerPersistenceDto snapshot)
    {
        if (_persistence is not { HasProvider: true } || _lifetime is null)
            return;

        try
        {
            await _persistence.SavePlayerAsync(snapshot, _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Logger.LogError(
                exception,
                "Failed to save Warcraft state for {SteamId}.",
                snapshot.SteamId);
        }
    }

    private static ulong? GetAttackerSteamId(CTakeDamageInfo damageInfo)
    {
        var attacker = damageInfo.Attacker.Value;

        if (attacker is CCSPlayerPawn pawn)
        {
            var controller = pawn.OriginalController.Value;
            return IsHuman(controller) ? controller!.SteamID : null;
        }

        if (attacker is CCSPlayerController controllerEntity)
            return IsHuman(controllerEntity) ? controllerEntity.SteamID : null;

        return null;
    }

    private static string? GetAttackerWeapon(CTakeDamageInfo damageInfo)
    {
        if (damageInfo.Attacker.Value is not CCSPlayerPawn pawn)
            return null;

        return pawn.WeaponServices?.ActiveWeapon.Value?.DesignerName;
    }

    private static bool IsHuman(CCSPlayerController? player)
        => player is { IsValid: true, IsBot: false } && player.SteamID != 0;
}
