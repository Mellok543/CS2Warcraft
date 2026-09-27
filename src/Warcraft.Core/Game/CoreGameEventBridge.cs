using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Warcraft.Api;
using Warcraft.Api.Events;

namespace Warcraft.Core.Game;

/// <summary>
/// CounterStrikeSharp -> Warcraft.Api event boundary.
/// Owns transient engine-only attribution state and translates game callbacks
/// into stable Warcraft events. No progression or persistence policy lives here.
/// </summary>
internal sealed class CoreGameEventBridge(
    Func<IWarcraftApi?> apiAccessor,
    AbilityDamageService abilityDamage,
    CoreConfig config)
{
    private readonly Dictionary<int, ulong> _creditedKills = [];
    private int _ticksSinceGameTick;

    public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (IsHuman(player))
            apiAccessor()?.Events.Publish(new PlayerSpawnEvent(player!.SteamID));

        return HookResult.Continue;
    }

    public HookResult OnPlayerJump(EventPlayerJump @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (IsHuman(player))
            apiAccessor()?.Events.Publish(new PlayerJumpEvent(player!.SteamID));

        return HookResult.Continue;
    }

    public void OnTick()
    {
        var api = apiAccessor();
        if (api is null)
            return;

        var interval = TimeSpan.FromMilliseconds(Math.Max(15, config.GameTickIntervalMilliseconds));
        var ticksPerEvent = Math.Max(
            1,
            (int)Math.Round(interval.TotalSeconds / Math.Max(Server.TickInterval, 0.001f)));

        if (++_ticksSinceGameTick < ticksPerEvent)
            return;

        _ticksSinceGameTick = 0;
        abilityDamage.PruneExpiredCredits();
        api.Events.Publish(new GameTickEvent(Server.CurrentTime, interval));
    }

    public HookResult OnPlayerHurt(EventPlayerHurt @event, GameEventInfo info)
    {
        var victim = @event.Userid;
        if (!IsHuman(victim))
            return HookResult.Continue;

        var humanVictim = victim!;
        var attacker = @event.Attacker;
        ulong? attackerSteamId = IsHuman(attacker) ? attacker!.SteamID : null;

        apiAccessor()?.Events.Publish(new PlayerHurtEvent(
            humanVictim.SteamID,
            attackerSteamId,
            @event.DmgHealth));

        return HookResult.Continue;
    }

    public HookResult OnPlayerDeathPre(EventPlayerDeath @event, GameEventInfo info)
    {
        var victim = @event.Userid;
        if (victim is not { IsValid: true } ||
            !abilityDamage.TryTakeCredit(victim.Slot, out var credit))
        {
            return HookResult.Continue;
        }

        var attacker = @event.Attacker;
        if (attacker is { IsValid: true } && attacker.Slot != victim.Slot)
            return HookResult.Continue;

        var killer = Utilities.GetPlayerFromSteamId(credit.AttackerSteamId);
        if (!IsHuman(killer))
            return HookResult.Continue;

        _creditedKills[victim.Slot] = killer!.SteamID;
        @event.Attacker = killer;
        return HookResult.Changed;
    }

    public HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        var victim = @event.Userid;
        var creditedKiller =
            victim is { IsValid: true } &&
            _creditedKills.Remove(victim.Slot, out var credited)
                ? Utilities.GetPlayerFromSteamId(credited)
                : null;

        if (!IsHuman(victim))
            return HookResult.Continue;

        var humanVictim = victim!;
        var attacker = creditedKiller ?? @event.Attacker;
        ulong? killerSteamId = IsHuman(attacker) ? attacker!.SteamID : null;

        apiAccessor()?.Events.Publish(new PlayerDeathEvent(
            humanVictim.SteamID,
            killerSteamId));

        if (killerSteamId.HasValue &&
            killerSteamId.Value != humanVictim.SteamID)
        {
            apiAccessor()?.Events.Publish(new PlayerKillEvent(
                killerSteamId.Value,
                humanVictim.SteamID,
                @event.Headshot,
                attacker!.TeamNum == humanVictim.TeamNum,
                @event.Weapon));
        }

        var assister = @event.Assister;
        if (IsHuman(assister) &&
            assister!.SteamID != humanVictim.SteamID &&
            assister.TeamNum != humanVictim.TeamNum)
        {
            apiAccessor()?.Events.Publish(new PlayerAssistEvent(
                assister.SteamID,
                humanVictim.SteamID,
                @event.Assistedflash));
        }

        return HookResult.Continue;
    }

    public HookResult OnWeaponFire(EventWeaponFire @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (IsHuman(player))
        {
            apiAccessor()?.Events.Publish(new WeaponFireEvent(
                player!.SteamID,
                @event.Weapon));
        }

        return HookResult.Continue;
    }

    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        abilityDamage.Clear();
        _creditedKills.Clear();
        apiAccessor()?.Events.Publish(new RoundStartEvent());
        return HookResult.Continue;
    }

    public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        var api = apiAccessor();
        if (api is null)
            return HookResult.Continue;

        foreach (var result in Utilities.GetPlayers()
                     .Where(x =>
                         IsHuman(x) &&
                         x.TeamNum is TeamTerrorist or TeamCounterTerrorist)
                     .Select(x =>
                         new PlayerRoundResultEvent(
                             x.SteamID,
                             x.TeamNum == @event.Winner)))
        {
            api.Events.Publish(result);
        }

        api.Events.Publish(new RoundEndEvent(@event.Winner));
        return HookResult.Continue;
    }

    public HookResult OnBombPlanted(EventBombPlanted @event, GameEventInfo info)
    {
        if (IsHuman(@event.Userid))
            apiAccessor()?.Events.Publish(new BombPlantedEvent(@event.Userid!.SteamID));

        return HookResult.Continue;
    }

    public HookResult OnBombDefused(EventBombDefused @event, GameEventInfo info)
    {
        if (IsHuman(@event.Userid))
            apiAccessor()?.Events.Publish(new BombDefusedEvent(@event.Userid!.SteamID));

        return HookResult.Continue;
    }

    public HookResult OnPlayerTakeDamagePre(
        CCSPlayerPawn victimPawn,
        CTakeDamageInfo damageInfo)
    {
        var victimController = victimPawn.OriginalController.Value;
        if (!IsHuman(victimController))
            return HookResult.Continue;

        var damageEvent = new DamagePreEvent
        {
            VictimSteamId = victimController!.SteamID,
            AttackerSteamId = GetAttackerSteamId(damageInfo),
            AttackerIsPlayer = damageInfo.Attacker.Value is CCSPlayerPawn,
            Damage = damageInfo.Damage,
            Weapon = GetAttackerWeapon(damageInfo),
            Kind = ToDamageKind(damageInfo)
        };

        var originalDamage = damageInfo.Damage;
        apiAccessor()?.Events.Publish(damageEvent);
        damageInfo.Damage = Math.Max(0.0f, damageEvent.Damage);

        return originalDamage > 0 && damageInfo.Damage <= 0
            ? HookResult.Handled
            : HookResult.Continue;
    }

    public void OnPlayerTakeDamagePost(
        CCSPlayerPawn victimPawn,
        CTakeDamageInfo damageInfo,
        CTakeDamageResult result)
    {
        var victimController = victimPawn.OriginalController.Value;
        if (!IsHuman(victimController))
            return;

        apiAccessor()?.Events.Publish(new DamagePostEvent(
            victimController!.SteamID,
            GetAttackerSteamId(damageInfo),
            result.HealthLost > 0 ? result.HealthLost : result.DamageDealt,
            GetAttackerWeapon(damageInfo),
            ToDamageKind(damageInfo)));
    }

    private static DamageKind ToDamageKind(CTakeDamageInfo damageInfo)
    {
        var bits = damageInfo.BitsDamageType;
        var kind = damageInfo.GetHitGroup() == HitGroup_t.HITGROUP_HEAD
            ? DamageKind.Headshot
            : DamageKind.None;

        if ((bits & (DamageTypes_t.DMG_BULLET | DamageTypes_t.DMG_BUCKSHOT)) != 0)
            kind |= DamageKind.Bullet;
        if ((bits & (DamageTypes_t.DMG_SLASH | DamageTypes_t.DMG_CLUB)) != 0)
            kind |= DamageKind.Melee;
        if ((bits & DamageTypes_t.DMG_FALL) != 0)
            kind |= DamageKind.Fall;
        if ((bits & (DamageTypes_t.DMG_BLAST | DamageTypes_t.DMG_BLAST_SURFACE)) != 0)
            kind |= DamageKind.Blast;
        if ((bits & DamageTypes_t.DMG_BURN) != 0)
            kind |= DamageKind.Burn;
        if ((bits & DamageTypes_t.DMG_HEADSHOT) != 0)
            kind |= DamageKind.Headshot;

        return kind;
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

    private const byte TeamTerrorist = 2;
    private const byte TeamCounterTerrorist = 3;

    private static bool IsHuman(CCSPlayerController? player)
        => player is { IsValid: true, IsBot: false } && player.SteamID != 0;
}
