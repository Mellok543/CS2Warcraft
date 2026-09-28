using System.Numerics;
using CounterStrikeSharp.API;
using Warcraft.Abilities.Game;
using Warcraft.Api.Abilities;
using Warcraft.Api.Events;

namespace Warcraft.Abilities.Passives;

/// <summary>Passive: speed boost after taking damage. Config: multiplier, duration (s).</summary>
internal sealed class AdrenalineAbility(MovementController movement) : AbilityHandler
{
    public override string Id => "adrenaline";
    protected override AbilityKind Kind => AbilityKind.Passive;
    protected override string Description => "После получения урона скорость x{multiplier} на {duration} с.";
    protected override string DisplayName => "Адреналин";

    protected override void Subscribe(IWarcraftEventBus events)
        => Track(events.Subscribe<DamagePostEvent>(OnDamagePost));

    private void OnDamagePost(DamagePostEvent @event)
    {
        if (@event.FinalDamage <= 0 ||
            GetUsable(@event.VictimSteamId) is not { } ability ||
            GamePlayers.FindAlive(@event.VictimSteamId) is not { } victim)
        {
            return;
        }

        movement.Boost(
            victim.Controller.Slot,
            (float)Math.Clamp(AbilityConfigReader.GetLevelDouble(ability, "multiplier", 1.2), 1, 3),
            Server.CurrentTime + Math.Clamp(AbilityConfigReader.GetLevelDouble(ability, "duration", 2), 0.1, 10));
    }
}

/// <summary>Passive: speed boost after a kill. Config: multiplier, duration (s).</summary>
internal sealed class KillSpeedAbility(MovementController movement) : AbilityHandler
{
    public override string Id => "kill_speed";
    protected override AbilityKind Kind => AbilityKind.Passive;
    protected override string Description => "После убийства скорость x{multiplier} на {duration} с.";
    protected override string DisplayName => "Жажда охоты";

    protected override void Subscribe(IWarcraftEventBus events)
        => Track(events.Subscribe<PlayerKillEvent>(OnKill));

    private void OnKill(PlayerKillEvent kill)
    {
        if (kill.TeamKill ||
            GetUsable(kill.KillerSteamId) is not { } ability ||
            GamePlayers.FindAlive(kill.KillerSteamId) is not { } killer)
        {
            return;
        }

        movement.Boost(
            killer.Controller.Slot,
            (float)Math.Clamp(AbilityConfigReader.GetLevelDouble(ability, "multiplier", 1.3), 1, 3),
            Server.CurrentTime + Math.Clamp(AbilityConfigReader.GetLevelDouble(ability, "duration", 3), 0.1, 10));
    }
}

/// <summary>
/// Passive: stronger jumps. Config: forward (horizontal speed multiplier, default 1),
/// up (extra vertical speed, default 0).
/// </summary>
internal sealed class JumpBoostAbility(IGameScheduler scheduler) : AbilityHandler
{
    private readonly Dictionary<ulong, double> _readyAt = [];

    public override string Id => "jump_boost";
    protected override AbilityKind Kind => AbilityKind.Passive;
    protected override string Description =>
        "Длинный прыжок: импульс x{forward|1}, максимум {maxSpeed|340} u/s, выше на {up|0}.";
    protected override string DisplayName => "Длинный прыжок";

    protected override void Subscribe(IWarcraftEventBus events)
        => Track(events.Subscribe<PlayerJumpEvent>(OnJump));

    private void OnJump(PlayerJumpEvent jump)
    {
        if (GetUsable(jump.SteamId) is not { } ability)
            return;

        var now = Server.CurrentTime;
        if (_readyAt.GetValueOrDefault(jump.SteamId) > now)
            return;

        var forward = (float)Math.Clamp(AbilityConfigReader.GetLevelDouble(ability, "forward", 1.0), 1, 2);
        var maxSpeed = (float)Math.Clamp(AbilityConfigReader.GetLevelDouble(ability, "maxSpeed", 340), 250, 500);
        var up = (float)Math.Clamp(AbilityConfigReader.GetLevelDouble(ability, "up", 0), 0, 400);
        var cooldown = Math.Clamp(AbilityConfigReader.GetLevelDouble(ability, "cooldown", 1.25), 0.25, 5);
        var steamId = jump.SteamId;

        _readyAt[steamId] = now + cooldown;

        // player_jump fires before CS2 has fully settled the jump velocity.
        // Apply the boost a few milliseconds later so the engine jump impulse is already present.
        scheduler.Schedule(0.04f, () =>
        {
            if (GamePlayers.FindAlive(steamId) is not { } player)
                return;

            var velocity = player.Pawn.AbsVelocity ?? Vector3.Zero;
            var horizontal = MathF.Sqrt(velocity.X * velocity.X + velocity.Y * velocity.Y);

            // A long jump must also work from a standing/slow start. If there is not
            // enough horizontal velocity yet, push in the player's look direction.
            var minSpeed = (float)Math.Clamp(
                AbilityConfigReader.GetLevelDouble(ability, "minSpeed", 250), 0, 500);
            var target = Math.Min(Math.Max(horizontal * forward, minSpeed), maxSpeed);

            float dirX;
            float dirY;
            if (horizontal >= 20f)
            {
                dirX = velocity.X / horizontal;
                dirY = velocity.Y / horizontal;
            }
            else
            {
                var yaw = player.Pawn.EyeAngles.Y * MathF.PI / 180f;
                dirX = MathF.Cos(yaw);
                dirY = MathF.Sin(yaw);
            }

            // Keep the normal CS2 jump impulse and add the configured vertical boost.
            var vertical = velocity.Z + up;

            player.Pawn.Teleport(velocity: new Vector3(
                dirX * target,
                dirY * target,
                vertical));
        });
    }
}


/// <summary>
/// Passive bunny-hop momentum assist. Config: multiplier (horizontal momentum gain),
/// maxSpeed (hard horizontal cap). The bonus is applied on every real jump event.
/// </summary>
internal sealed class BhopAbility(IGameScheduler scheduler) : AbilityHandler
{
    public override string Id => "bhop";
    protected override AbilityKind Kind => AbilityKind.Passive;
    protected override string Description =>
        "Бхоп: при каждом прыжке сохраняет разгон x{multiplier|1.04}, максимум {maxSpeed|380} u/s.";
    protected override string DisplayName => "Бхоп";

    protected override void Subscribe(IWarcraftEventBus events)
        => Track(events.Subscribe<PlayerJumpEvent>(OnJump));

    private void OnJump(PlayerJumpEvent jump)
    {
        if (GetUsable(jump.SteamId) is not { } ability)
            return;

        var multiplier = (float)Math.Clamp(
            AbilityConfigReader.GetLevelDouble(ability, "multiplier", 1.04), 1.0, 1.25);
        var maxSpeed = (float)Math.Clamp(
            AbilityConfigReader.GetLevelDouble(ability, "maxSpeed", 380), 260, 520);
        var steamId = jump.SteamId;

        scheduler.Schedule(0.04f, () =>
        {
            if (GamePlayers.FindAlive(steamId) is not { } player)
                return;

            var velocity = player.Pawn.AbsVelocity ?? Vector3.Zero;
            var horizontal = MathF.Sqrt(velocity.X * velocity.X + velocity.Y * velocity.Y);
            if (horizontal < 20f || horizontal >= maxSpeed)
                return;

            var target = Math.Min(horizontal * multiplier, maxSpeed);
            var scale = target / horizontal;

            player.Pawn.Teleport(velocity: new Vector3(
                velocity.X * scale,
                velocity.Y * scale,
                velocity.Z));
        });
    }
}
