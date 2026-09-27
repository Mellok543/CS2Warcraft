using CounterStrikeSharp.API;
using Warcraft.Abilities.Game;
using Warcraft.Api.Abilities;
using Warcraft.Api.Events;

namespace Warcraft.Abilities;

/// <summary>
/// Passive: chance on hit to stun (freeze or slow) the enemy.
/// Config: chance, duration (seconds), slow (movement multiplier while stunned, default 0).
/// </summary>
internal sealed class BashAbility(MovementController movement) : AbilityHandler
{
    public override string Id => "bash";
    protected override AbilityKind Kind => AbilityKind.Passive;
    protected override string Description =>
        "С шансом {chance%} оглушает врага на {duration} с.";
    protected override string DisplayName => "Оглушение";

    protected override void Subscribe(IWarcraftEventBus events)
        => Track(events.Subscribe<DamagePostEvent>(OnDamagePost));

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
        movement.Stun(victim, slow, Server.CurrentTime + duration);
        Fx?.BurstOn(victim, FxColor.Storm);
        Fx?.Status(Id, victim, WarcraftParticles.Body(FxColor.Storm), duration);
    }
}
