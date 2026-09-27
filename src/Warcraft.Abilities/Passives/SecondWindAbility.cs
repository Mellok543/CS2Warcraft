using Warcraft.Abilities.Game;
using Warcraft.Api.Abilities;
using Warcraft.Api.Events;

namespace Warcraft.Abilities.Passives;

/// <summary>
/// Passive: once per round, when health drops below a threshold, instantly heals.
/// Config: threshold (HP share, default 0.3), amount.
/// </summary>
internal sealed class SecondWindAbility : AbilityHandler
{
    private readonly HashSet<ulong> _usedThisRound = [];

    public override string Id => "second_wind";
    protected override AbilityKind Kind => AbilityKind.Passive;
    protected override string Description =>
        "Раз в раунд при HP ниже {threshold%|0.3} мгновенно восстанавливает {amount} HP.";
    protected override string DisplayName => "Второе дыхание";

    protected override void Subscribe(IWarcraftEventBus events)
    {
        Track(events.Subscribe<DamagePostEvent>(OnDamagePost));
        Track(events.Subscribe<RoundStartEvent>(_ => _usedThisRound.Clear()));
    }

    private void OnDamagePost(DamagePostEvent @event)
    {
        if (@event.FinalDamage <= 0 ||
            _usedThisRound.Contains(@event.VictimSteamId) ||
            GamePlayers.FindAlive(@event.VictimSteamId) is not { } victim ||
            GetUsable(@event.VictimSteamId) is not { } ability)
        {
            return;
        }

        var threshold = Math.Clamp(AbilityConfigReader.GetLevelDouble(ability, "threshold", 0.3), 0, 1);
        if (victim.Pawn.MaxHealth <= 0 || (double)victim.Pawn.Health / victim.Pawn.MaxHealth > threshold)
            return;

        _usedThisRound.Add(@event.VictimSteamId);
        var healed = PlayerHealth.Heal(victim.Pawn, AbilityConfigReader.GetLevelInt(ability, "amount", 30));
        Api?.Events.Publish(new AbilityTelemetryEvent(@event.VictimSteamId, Id, AbilityTelemetryKind.Triggered));
        if (healed > 0)
            Api?.Events.Publish(new AbilityTelemetryEvent(@event.VictimSteamId, Id, AbilityTelemetryKind.Healing, healed));
        victim.Controller.PrintToChat(" [Warcraft] Второе дыхание!");
    }
}
