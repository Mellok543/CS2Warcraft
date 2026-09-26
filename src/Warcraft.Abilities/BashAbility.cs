using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Warcraft.Abilities.Game;
using Warcraft.Api.Abilities;
using Warcraft.Api.Events;

namespace Warcraft.Abilities;

/// <summary>
/// Passive: chance on hit to stun (freeze or slow) the enemy.
/// Config: chance, duration (seconds), slow (movement multiplier while stunned, default 0).
/// </summary>
internal sealed class BashAbility : AbilityHandler
{
    private readonly Dictionary<ulong, Stun> _stunned = [];

    public override string Id => "bash";
    protected override AbilityKind Kind => AbilityKind.Passive;
    protected override string Description =>
        "С шансом {chance%} оглушает врага на {duration} с.";
    protected override string DisplayName => "Оглушение";

    protected override void Subscribe(IWarcraftEventBus events)
    {
        Track(events.Subscribe<DamagePostEvent>(OnDamagePost));
        Track(events.Subscribe<GameTickEvent>(OnGameTick));
        Track(events.Subscribe<RoundStartEvent>(_ => _stunned.Clear()));
    }

    protected override void OnDisposed() => _stunned.Clear();

    private void OnDamagePost(DamagePostEvent @event)
    {
        if (@event.AttackerSteamId is not { } attacker ||
            attacker == @event.VictimSteamId ||
            @event.FinalDamage <= 0)
        {
            return;
        }

        var ability = GetUsable(attacker);
        if (ability is null)
            return;

        var chance = Math.Clamp(AbilityConfigReader.GetLevelDouble(ability, "chance"), 0.0, 1.0);
        if (Random.Shared.NextDouble() >= chance)
            return;

        if (GamePlayers.FindAlive(attacker) is not { } source ||
            GamePlayers.FindAlive(@event.VictimSteamId) is not { } victim ||
            !GamePlayers.AreEnemies(source, victim))
        {
            return;
        }

        var duration = Math.Clamp(AbilityConfigReader.GetLevelDouble(ability, "duration", 0.5), 0.05, 5.0);
        var slow = (float)Math.Clamp(AbilityConfigReader.GetLevelDouble(ability, "slow", 0.0), 0.0, 1.0);

        _stunned[@event.VictimSteamId] = new Stun(Server.CurrentTime + duration, slow);
        SetModifier(victim.Pawn, slow);
    }

    /// <summary>The engine restores the modifier over time; hold it until the stun expires.</summary>
    private void OnGameTick(GameTickEvent tick)
    {
        foreach (var (steamId, stun) in _stunned.ToArray())
        {
            if (tick.ServerTime >= stun.Until || GamePlayers.FindAlive(steamId) is not { } victim)
            {
                _stunned.Remove(steamId);
                continue;
            }

            SetModifier(victim.Pawn, stun.Modifier);
        }
    }

    private static void SetModifier(CCSPlayerPawn pawn, float value)
    {
        pawn.VelocityModifier = value;
        Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_flVelocityModifier");
    }

    private readonly record struct Stun(double Until, float Modifier);
}
