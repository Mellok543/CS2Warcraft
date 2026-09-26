using System.Numerics;
using CounterStrikeSharp.API;
using Warcraft.Abilities.Game;
using Warcraft.Api.Abilities;
using Warcraft.Api.Combat;
using Warcraft.Api.Events;

namespace Warcraft.Abilities.Actives;

/// <summary>
/// Active: returns the caster to where they stood a few seconds ago (a position
/// they actually occupied, so it is always valid). Config: seconds, cooldown.
/// </summary>
internal sealed class RecallAbility(PositionHistory history) : ActiveAbilityHandler
{
    public override string Id => "recall";
    protected override string Description =>
        "Возвращает вас туда, где вы были {seconds|3} с назад. Перезарядка {cooldown} с.";
    protected override string DisplayName => "Откат во времени";

    protected override void Activate(AbilityActivationEvent activation, LivePlayer caster)
    {
        var seconds = Math.Clamp(AbilityConfigReader.GetLevelDouble(activation.Ability, "seconds", 3), 0.5, 10);
        var position = history.GetAgo(caster.Controller.Slot, seconds, Server.CurrentTime);

        if (position is null)
        {
            activation.Fail("Нет сохранённой позиции.");
            return;
        }

        caster.Pawn.Teleport(position: position.Value, velocity: Vector3.Zero);
        activation.Succeed();
    }
}

/// <summary>Active: swaps places with the nearest enemy in range. Config: range, cooldown.</summary>
internal sealed class SwapAbility : ActiveAbilityHandler
{
    public override string Id => "swap";
    protected override string Description =>
        "Меняется местами с ближайшим врагом в радиусе {range}. Перезарядка {cooldown} с.";
    protected override string DisplayName => "Подмена";

    protected override void Activate(AbilityActivationEvent activation, LivePlayer caster)
    {
        var range = (float)Math.Max(0, AbilityConfigReader.GetLevelDouble(activation.Ability, "range", 800));
        if (GamePlayers.NearestEnemy(caster, range) is not { } target)
        {
            activation.Fail($"Нет врагов в радиусе {range:0}.");
            return;
        }

        var casterPosition = caster.Position;
        var targetPosition = target.Position;
        caster.Pawn.Teleport(position: targetPosition, velocity: Vector3.Zero);
        target.Pawn.Teleport(position: casterPosition, velocity: Vector3.Zero);

        activation.Succeed($"Вы поменялись местами с {target.Controller.PlayerName}.");
    }
}

/// <summary>Active: pulls the nearest enemy towards the caster. Config: range, force, upForce, cooldown.</summary>
internal sealed class PullAbility : ActiveAbilityHandler
{
    public override string Id => "pull";
    protected override string Description =>
        "Притягивает ближайшего врага в радиусе {range} к себе. Перезарядка {cooldown} с.";
    protected override string DisplayName => "Крюк";

    protected override void Activate(AbilityActivationEvent activation, LivePlayer caster)
    {
        var range = (float)Math.Max(0, AbilityConfigReader.GetLevelDouble(activation.Ability, "range", 700));
        var force = (float)Math.Clamp(AbilityConfigReader.GetLevelDouble(activation.Ability, "force", 700), 0, 3000);
        var upForce = (float)Math.Clamp(AbilityConfigReader.GetLevelDouble(activation.Ability, "upForce", 250), 0, 1500);

        if (GamePlayers.NearestEnemy(caster, range) is not { } target)
        {
            activation.Fail($"Нет врагов в радиусе {range:0}.");
            return;
        }

        target.Pawn.Teleport(velocity: Push.Towards(target.Position, caster.Position, force, upForce));
        activation.Succeed($"{target.Controller.PlayerName} притянут.");
    }
}

/// <summary>Active: knocks back (and optionally damages) enemies around. Config: radius, force, upForce, damage, cooldown.</summary>
internal sealed class RepulseAbility : ActiveAbilityHandler
{
    public override string Id => "repulse";
    protected override string Description =>
        "Отбрасывает врагов в радиусе {radius} и наносит {damage|0} урона. Перезарядка {cooldown} с.";
    protected override string DisplayName => "Ударная волна";

    protected override void Activate(AbilityActivationEvent activation, LivePlayer caster)
    {
        var radius = (float)Math.Max(0, AbilityConfigReader.GetLevelDouble(activation.Ability, "radius", 300));
        var force = (float)Math.Clamp(AbilityConfigReader.GetLevelDouble(activation.Ability, "force", 700), 0, 3000);
        var upForce = (float)Math.Clamp(AbilityConfigReader.GetLevelDouble(activation.Ability, "upForce", 300), 0, 1500);
        var damage = AbilityConfigReader.GetLevelInt(activation.Ability, "damage");

        var targets = GamePlayers.EnemiesAround(caster, caster.Position, radius).ToArray();
        if (targets.Length == 0)
        {
            activation.Fail($"Нет врагов в радиусе {radius:0}.");
            return;
        }

        foreach (var target in targets)
        {
            target.Pawn.Teleport(velocity: Push.Towards(caster.Position, target.Position, force, upForce));

            if (damage > 0)
            {
                Api?.Combat.DealAbilityDamage(
                    new AbilityDamageRequest(activation.SteamId, target.Controller.Slot, damage, Id));
            }
        }

        activation.Succeed($"Отброшено врагов: {targets.Length}.");
    }
}

/// <summary>Active: near-invisibility for a few seconds. Config: alpha (default 0), duration, cooldown.</summary>
internal sealed class CloakAbility : ActiveAbilityHandler
{
    private readonly Dictionary<int, Cloak> _cloaked = [];

    public override string Id => "cloak";
    protected override string Description =>
        "Становится невидимым на {duration} с. Перезарядка {cooldown} с.";
    protected override string DisplayName => "Маскировка";

    protected override void SubscribeExtra(IWarcraftEventBus events)
    {
        Track(events.Subscribe<GameTickEvent>(OnGameTick));
        Track(events.Subscribe<RoundStartEvent>(_ => _cloaked.Clear()));
    }

    protected override void OnDisposed()
    {
        foreach (var slot in _cloaked.Keys)
        {
            if (GamePlayers.FindAliveBySlot(slot) is { } player)
                PlayerRender.SetAlpha(player.Pawn, PlayerRender.Opaque);
        }

        _cloaked.Clear();
    }

    protected override void Activate(AbilityActivationEvent activation, LivePlayer caster)
    {
        var alpha = Math.Clamp(AbilityConfigReader.GetLevelInt(activation.Ability, "alpha"), 0, PlayerRender.Opaque);
        var duration = Math.Clamp(AbilityConfigReader.GetLevelDouble(activation.Ability, "duration", 4), 0.1, 30);

        _cloaked[caster.Controller.Slot] = new Cloak(Server.CurrentTime + duration, alpha);
        PlayerRender.SetAlpha(caster.Pawn, alpha);
        activation.Succeed($"Маскировка на {duration:0.#} с.");
    }

    private void OnGameTick(GameTickEvent tick)
    {
        foreach (var (slot, cloak) in _cloaked.ToArray())
        {
            var player = GamePlayers.FindAliveBySlot(slot);
            if (player is null || tick.ServerTime >= cloak.Until)
            {
                _cloaked.Remove(slot);
                if (player is { } visible)
                    PlayerRender.SetAlpha(visible.Pawn, PlayerRender.Opaque);
                continue;
            }

            PlayerRender.SetAlpha(player.Value.Pawn, cloak.Alpha);
        }
    }

    private readonly record struct Cloak(double Until, int Alpha);
}

/// <summary>
/// Active: the caster and nearby teammates deal more damage for a while.
/// Config: percent, duration, radius, cooldown.
/// </summary>
internal sealed class BattleCryAbility : ActiveAbilityHandler
{
    private readonly Dictionary<ulong, Buff> _buffs = [];

    public override string Id => "battle_cry";
    protected override string Description =>
        "Вы и союзники в радиусе {radius} наносите на {percent%} больше урона {duration} с. Перезарядка {cooldown} с.";
    protected override string DisplayName => "Боевой клич";

    protected override void SubscribeExtra(IWarcraftEventBus events)
    {
        Track(events.Subscribe<DamagePreEvent>(OnDamagePre));
        Track(events.Subscribe<RoundStartEvent>(_ => _buffs.Clear()));
    }

    protected override void OnDisposed() => _buffs.Clear();

    protected override void Activate(AbilityActivationEvent activation, LivePlayer caster)
    {
        var percent = (float)Math.Clamp(AbilityConfigReader.GetLevelDouble(activation.Ability, "percent", 0.2), 0, 3);
        var duration = Math.Clamp(AbilityConfigReader.GetLevelDouble(activation.Ability, "duration", 5), 0.1, 30);
        var radius = (float)Math.Max(0, AbilityConfigReader.GetLevelDouble(activation.Ability, "radius", 400));
        var until = Server.CurrentTime + duration;

        var allies = GamePlayers.AlliesAround(caster, caster.Position, radius)
            .Where(x => !x.Controller.IsBot && x.Controller.SteamID != 0)
            .ToArray();

        foreach (var ally in allies)
        {
            _buffs[ally.Controller.SteamID] = new Buff(until, percent);
            if (ally.Controller.SteamID != activation.SteamId)
                ally.Controller.PrintToChat($" [Warcraft] Боевой клич: +{percent * 100:0}% урона на {duration:0.#} с.");
        }

        activation.Succeed($"Боевой клич: союзников {allies.Length}.");
    }

    private void OnDamagePre(DamagePreEvent @event)
    {
        if (@event.AttackerSteamId is not { } attacker ||
            attacker == @event.VictimSteamId ||
            !_buffs.TryGetValue(attacker, out var buff))
        {
            return;
        }

        if (Server.CurrentTime >= buff.Until)
        {
            _buffs.Remove(attacker);
            return;
        }

        @event.Damage *= 1f + buff.Percent;
    }

    private readonly record struct Buff(double Until, float Percent);
}

/// <summary>
/// Active: revives a dead teammate at their spawn (humans first). Config: cooldown.
/// Best used as an ultimate.
/// </summary>
internal sealed class ResurrectAbility : ActiveAbilityHandler
{
    public override string Id => "resurrect";
    protected override string Description => "Воскрешает павшего союзника. Перезарядка {cooldown} с.";
    protected override string DisplayName => "Воскрешение";

    protected override void Activate(AbilityActivationEvent activation, LivePlayer caster)
    {
        var dead = Utilities.GetPlayers()
            .Where(x => x.IsValid && !x.PawnIsAlive && x.TeamNum == caster.Team && x.Slot != caster.Controller.Slot)
            .OrderBy(x => x.IsBot)
            .FirstOrDefault();

        if (dead is null)
        {
            activation.Fail("Нет павших союзников.");
            return;
        }

        dead.Respawn();
        dead.PrintToChat($" [Warcraft] Вас воскресил {caster.Controller.PlayerName}!");
        activation.Succeed($"Воскрешён {dead.PlayerName}.");
    }
}

internal static class Push
{
    /// <summary>Horizontal velocity from <paramref name="from"/> towards <paramref name="to"/> plus a lift.</summary>
    public static Vector3 Towards(Vector3 from, Vector3 to, float force, float upForce)
    {
        var direction = new Vector2(to.X - from.X, to.Y - from.Y);
        direction = direction.LengthSquared() < 1f ? Vector2.UnitX : Vector2.Normalize(direction);
        return new Vector3(direction.X * force, direction.Y * force, upForce);
    }
}
