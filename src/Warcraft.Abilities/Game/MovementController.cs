using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace Warcraft.Abilities.Game;

/// <summary>
/// Single owner of <c>m_flVelocityModifier</c> for all abilities, so speed
/// passives, temporary boosts and stuns never overwrite each other.
/// Priority: stun &gt; max(passive speed, boost) &gt; engine default.
/// Keyed by player slot; game thread only.
/// </summary>
internal sealed class MovementController
{
    private const float Epsilon = 0.001f;
    private const float Normal = 1.0f;

    private readonly Dictionary<int, float> _passive = [];
    private readonly Dictionary<int, TimedModifier> _boosts = [];
    private readonly Dictionary<int, TimedModifier> _stuns = [];
    private readonly HashSet<int> _raised = [];

    public void SetPassive(int slot, float? multiplier)
    {
        if (multiplier is > Normal)
            _passive[slot] = multiplier.Value;
        else
            _passive.Remove(slot);
    }

    public void Boost(int slot, float multiplier, double until)
        => _boosts[slot] = new TimedModifier(until, multiplier);

    /// <summary>Holds the movement modifier at <paramref name="modifier"/> (0 = frozen) until the given time.</summary>
    public void Stun(LivePlayer target, float modifier, double until)
    {
        var slot = target.Controller.Slot;
        if (_stuns.TryGetValue(slot, out var existing) && existing.Until > until)
            return;

        _stuns[slot] = new TimedModifier(until, modifier);
        Set(target.Pawn, modifier);
    }

    public void Update(double now)
    {
        var slots = _passive.Keys.Concat(_boosts.Keys).Concat(_stuns.Keys).Concat(_raised).Distinct().ToArray();

        foreach (var slot in slots)
        {
            if (GamePlayers.FindAliveBySlot(slot) is not { } player)
            {
                Forget(slot);
                continue;
            }

            if (_stuns.TryGetValue(slot, out var stun))
            {
                if (now < stun.Until)
                {
                    Set(player.Pawn, stun.Modifier);
                    continue;
                }

                _stuns.Remove(slot);
                Set(player.Pawn, Desired(slot, now));
                continue;
            }

            ApplySpeed(slot, player.Pawn, Desired(slot, now));
        }
    }

    public void Clear()
    {
        _passive.Clear();
        _boosts.Clear();
        _stuns.Clear();
        _raised.Clear();
    }

    private float Desired(int slot, double now)
    {
        var desired = _passive.GetValueOrDefault(slot, Normal);

        if (_boosts.TryGetValue(slot, out var boost))
        {
            if (now < boost.Until)
                desired = Math.Max(desired, boost.Modifier);
            else
                _boosts.Remove(slot);
        }

        return desired;
    }

    private void ApplySpeed(int slot, CCSPlayerPawn pawn, float desired)
    {
        var current = pawn.VelocityModifier;

        if (desired > Normal + Epsilon)
        {
            // Below 1.0 the engine applies damage slowdown; let it recover first.
            if (current >= Normal - Epsilon && Math.Abs(current - desired) > Epsilon)
                Set(pawn, desired);

            _raised.Add(slot);
            return;
        }

        if (_raised.Remove(slot) && current > Normal + Epsilon)
            Set(pawn, Normal);
    }

    private void Forget(int slot)
    {
        _passive.Remove(slot);
        _boosts.Remove(slot);
        _stuns.Remove(slot);
        _raised.Remove(slot);
    }

    private static void Set(CCSPlayerPawn pawn, float value)
    {
        pawn.VelocityModifier = value;
        Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_flVelocityModifier");
    }

    private readonly record struct TimedModifier(double Until, float Modifier);
}
