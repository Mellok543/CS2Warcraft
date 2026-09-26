using Warcraft.Api.Events;

namespace Warcraft.Core.Game;

/// <summary>
/// Lets defensive modifiers (shields, damage reduction, cheat death, evasion)
/// react to ability damage without enabling on-hit chains:
/// <list type="bullet">
/// <item>publishes a <see cref="DamagePreEvent"/> with <see cref="DamageKind.Ability"/>;</item>
/// <item>never publishes a <see cref="DamagePostEvent"/> (no reflect/vampirism/poison on ability damage);</item>
/// <item>nested ability damage raised from a DamagePre handler skips the pipeline, so recursion is bounded.</item>
/// </list>
/// Game thread only.
/// </summary>
internal sealed class AbilityDamagePipeline(IWarcraftEventBus events)
{
    private int _depth;

    /// <param name="victimSteamId">Null for bots: no Warcraft modifiers apply to them.</param>
    public int Resolve(ulong attackerSteamId, ulong? victimSteamId, int amount, string abilityId)
    {
        if (amount <= 0)
            return 0;

        if (victimSteamId is not { } victim || _depth > 0)
            return amount;

        var damage = new DamagePreEvent
        {
            VictimSteamId = victim,
            AttackerSteamId = attackerSteamId,
            AttackerIsPlayer = true,
            Damage = amount,
            Weapon = abilityId,
            Kind = DamageKind.Ability,
            AbilityId = abilityId
        };

        _depth++;
        try
        {
            events.Publish(damage);
        }
        finally
        {
            _depth--;
        }

        return Math.Max(0, (int)Math.Round(damage.Damage));
    }
}
