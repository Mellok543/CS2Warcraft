namespace Warcraft.Api.Combat;

/// <summary>
/// Damage dealt by ability mechanics outside the weapon damage pipeline.
/// Core applies it and credits lethal hits to the attacker (kill feed, kill XP,
/// statistics) instead of counting them as suicides. Game thread only.
/// </summary>
public interface ICombatApi
{
    AbilityDamageResult DealAbilityDamage(AbilityDamageRequest request);
}

/// <param name="VictimSlot">Player slot of the victim (engine player index, humans and bots).</param>
public sealed record AbilityDamageRequest(
    ulong AttackerSteamId,
    int VictimSlot,
    int Amount,
    string AbilityId);

public sealed record AbilityDamageResult(bool Applied, bool Killed, int HealthRemoved)
{
    public static AbilityDamageResult NotApplied { get; } = new(false, false, 0);
}
