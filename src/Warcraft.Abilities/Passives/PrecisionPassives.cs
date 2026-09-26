using System.Numerics;
using CounterStrikeSharp.API;
using Warcraft.Abilities.Game;
using Warcraft.Api.Abilities;
using Warcraft.Api.Events;

namespace Warcraft.Abilities.Passives;

/// <summary>Passive: extra damage on headshots. Config: percent.</summary>
internal sealed class HeadshotDamageAbility : AbilityHandler
{
    public override string Id => "headshot_damage";
    protected override AbilityKind Kind => AbilityKind.Passive;
    protected override string Description => "Попадания в голову наносят на {percent%} больше урона.";
    protected override string DisplayName => "Меткость";

    protected override void Subscribe(IWarcraftEventBus events)
        => Track(events.Subscribe<DamagePreEvent>(OnDamagePre));

    private void OnDamagePre(DamagePreEvent @event)
    {
        if ((@event.Kind & DamageKind.Headshot) == 0 ||
            @event.AttackerSteamId is not { } attacker ||
            attacker == @event.VictimSteamId ||
            GetUsable(attacker) is not { } ability)
        {
            return;
        }

        @event.Damage *= (float)(1.0 + Math.Clamp(AbilityConfigReader.GetLevelDouble(ability, "percent"), 0, 5));
    }
}

/// <summary>Passive: extra damage when hitting an enemy from behind. Config: percent.</summary>
internal sealed class BackstabAbility : AbilityHandler
{
    /// <summary>cos(60°): the attacker is within ±60° behind the victim.</summary>
    private const float BehindThreshold = -0.5f;

    public override string Id => "backstab";
    protected override AbilityKind Kind => AbilityKind.Passive;
    protected override string Description => "Удар в спину наносит на {percent%} больше урона.";
    protected override string DisplayName => "Удар в спину";

    protected override void Subscribe(IWarcraftEventBus events)
        => Track(events.Subscribe<DamagePreEvent>(OnDamagePre));

    private void OnDamagePre(DamagePreEvent @event)
    {
        if (@event.AttackerSteamId is not { } attacker ||
            attacker == @event.VictimSteamId ||
            GetUsable(attacker) is not { } ability ||
            GamePlayers.FindAlive(attacker) is not { } source ||
            GamePlayers.FindAlive(@event.VictimSteamId) is not { } victim)
        {
            return;
        }

        var yaw = victim.Pawn.EyeAngles.Y * MathF.PI / 180f;
        var facing = new Vector2(MathF.Cos(yaw), MathF.Sin(yaw));
        var offset = source.Position - victim.Position;
        var toAttacker = new Vector2(offset.X, offset.Y);

        if (toAttacker.LengthSquared() < 1f ||
            Vector2.Dot(facing, Vector2.Normalize(toAttacker)) > BehindThreshold)
        {
            return;
        }

        @event.Damage *= (float)(1.0 + Math.Clamp(AbilityConfigReader.GetLevelDouble(ability, "percent"), 0, 5));
    }
}

/// <summary>Passive: extra damage against wounded enemies. Config: percent, threshold (victim HP share, default 0.35).</summary>
internal sealed class ExecuteAbility : AbilityHandler
{
    public override string Id => "execute";
    protected override AbilityKind Kind => AbilityKind.Passive;
    protected override string Description =>
        "Наносит на {percent%} больше урона врагам с HP ниже {threshold%|0.35}.";
    protected override string DisplayName => "Добивание";

    protected override void Subscribe(IWarcraftEventBus events)
        => Track(events.Subscribe<DamagePreEvent>(OnDamagePre));

    private void OnDamagePre(DamagePreEvent @event)
    {
        if (@event.AttackerSteamId is not { } attacker ||
            attacker == @event.VictimSteamId ||
            GetUsable(attacker) is not { } ability ||
            GamePlayers.FindAlive(@event.VictimSteamId) is not { } victim ||
            victim.Pawn.MaxHealth <= 0)
        {
            return;
        }

        var threshold = Math.Clamp(AbilityConfigReader.GetLevelDouble(ability, "threshold", 0.35), 0, 1);
        if ((double)victim.Pawn.Health / victim.Pawn.MaxHealth > threshold)
            return;

        @event.Damage *= (float)(1.0 + Math.Clamp(AbilityConfigReader.GetLevelDouble(ability, "percent"), 0, 5));
    }
}

/// <summary>Passive: reduces explosion damage (grenades, bomb). Config: percent.</summary>
internal sealed class BlastResistAbility : AbilityHandler
{
    public override string Id => "blast_resist";
    protected override AbilityKind Kind => AbilityKind.Passive;
    protected override string Description => "Снижает урон от взрывов на {percent%}.";
    protected override string DisplayName => "Взрывоустойчивость";

    protected override void Subscribe(IWarcraftEventBus events)
        => Track(events.Subscribe<DamagePreEvent>(OnDamagePre));

    private void OnDamagePre(DamagePreEvent @event)
    {
        if ((@event.Kind & DamageKind.Blast) == 0 || GetUsable(@event.VictimSteamId) is not { } ability)
            return;

        @event.Damage *= (float)(1.0 - Math.Clamp(AbilityConfigReader.GetLevelDouble(ability, "percent"), 0, 1));
    }
}

/// <summary>
/// Passive: a lethal hit leaves the player at 1 HP instead (chance, once per round).
/// Registered last among damage modifiers so it sees the final damage. Config: chance.
/// </summary>
internal sealed class CheatDeathAbility : AbilityHandler
{
    private readonly HashSet<ulong> _usedThisRound = [];

    public override string Id => "cheat_death";
    protected override AbilityKind Kind => AbilityKind.Passive;
    protected override string Description =>
        "С шансом {chance%} переживает смертельный удар с 1 HP (раз в раунд).";
    protected override string DisplayName => "Обман смерти";

    protected override void Subscribe(IWarcraftEventBus events)
    {
        Track(events.Subscribe<DamagePreEvent>(OnDamagePre));
        Track(events.Subscribe<RoundStartEvent>(_ => _usedThisRound.Clear()));
    }

    private void OnDamagePre(DamagePreEvent @event)
    {
        if (_usedThisRound.Contains(@event.VictimSteamId) ||
            GamePlayers.FindAlive(@event.VictimSteamId) is not { } victim ||
            @event.Damage < victim.Pawn.Health ||
            GetUsable(@event.VictimSteamId) is not { } ability ||
            Random.Shared.NextDouble() >= Math.Clamp(AbilityConfigReader.GetLevelDouble(ability, "chance"), 0, 1))
        {
            return;
        }

        _usedThisRound.Add(@event.VictimSteamId);
        @event.Damage = Math.Max(0, victim.Pawn.Health - 1);
        victim.Controller.PrintToChat(" [Warcraft] Обман смерти спас вас!");
    }
}

/// <summary>Passive: steals money from enemies on hit. Config: chance (default 1), amount.</summary>
internal sealed class MoneyStealAbility : AbilityHandler
{
    public override string Id => "money_steal";
    protected override AbilityKind Kind => AbilityKind.Passive;
    protected override string Description => "С шансом {chance%|1} крадёт ${amount} при попадании.";
    protected override string DisplayName => "Карманник";

    protected override void Subscribe(IWarcraftEventBus events)
        => Track(events.Subscribe<DamagePostEvent>(OnDamagePost));

    private void OnDamagePost(DamagePostEvent @event)
    {
        if (@event.AttackerSteamId is not { } attacker ||
            attacker == @event.VictimSteamId ||
            GetUsable(attacker) is not { } ability ||
            Random.Shared.NextDouble() >= Math.Clamp(AbilityConfigReader.GetLevelDouble(ability, "chance", 1.0), 0, 1))
        {
            return;
        }

        var thief = Utilities.GetPlayerFromSteamId(attacker);
        var victim = Utilities.GetPlayerFromSteamId(@event.VictimSteamId);
        var thiefMoney = thief?.InGameMoneyServices;
        var victimMoney = victim?.InGameMoneyServices;

        if (thief is not { IsValid: true } || victim is not { IsValid: true } ||
            thiefMoney is null || victimMoney is null || thief.TeamNum == victim.TeamNum)
        {
            return;
        }

        var stolen = Math.Min(victimMoney.Account, Math.Max(0, AbilityConfigReader.GetLevelInt(ability, "amount")));
        if (stolen <= 0)
            return;

        victimMoney.Account -= stolen;
        thiefMoney.Account = Math.Min(16000, thiefMoney.Account + stolen);
        Utilities.SetStateChanged(victim, "CCSPlayerController", "m_pInGameMoneyServices");
        Utilities.SetStateChanged(thief, "CCSPlayerController", "m_pInGameMoneyServices");
    }
}

/// <summary>Passive: bonus money on every spawn. Config: money.</summary>
internal sealed class SpawnMoneyAbility : AbilityHandler
{
    public override string Id => "spawn_money";
    protected override AbilityKind Kind => AbilityKind.Passive;
    protected override string Description => "При возрождении даёт ${money}.";
    protected override string DisplayName => "Жалование";

    protected override void Subscribe(IWarcraftEventBus events)
        => Track(events.Subscribe<PlayerSpawnEvent>(OnSpawn));

    private void OnSpawn(PlayerSpawnEvent spawn)
    {
        if (GetUsable(spawn.SteamId) is not { } ability)
            return;

        var player = Utilities.GetPlayerFromSteamId(spawn.SteamId);
        var money = player?.InGameMoneyServices;
        if (player is not { IsValid: true } || money is null)
            return;

        money.Account = Math.Min(16000, money.Account + Math.Max(0, AbilityConfigReader.GetLevelInt(ability, "money")));
        Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInGameMoneyServices");
    }
}
