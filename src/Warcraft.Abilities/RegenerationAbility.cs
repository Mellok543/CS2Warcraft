using Warcraft.Abilities.Game;
using Warcraft.Api.Abilities;
using Warcraft.Api.Events;

namespace Warcraft.Abilities;

/// <summary>
/// Passive: restores health periodically up to max health.
/// Config: amount (HP per pulse), interval (seconds between pulses, default 1).
/// </summary>
internal sealed class RegenerationAbility : AbilityHandler
{
    private const double MinimumInterval = 0.1;

    private readonly Dictionary<ulong, double> _nextPulseAt = [];

    public override string Id => "regeneration";
    protected override AbilityKind Kind => AbilityKind.Passive;
    protected override string Description =>
        "Восстанавливает {amount} HP каждые {interval|1} с.";
    protected override string DisplayName => "Регенерация";

    protected override void Subscribe(IWarcraftEventBus events)
    {
        Track(events.Subscribe<GameTickEvent>(OnGameTick));
        Track(events.Subscribe<RoundStartEvent>(_ => _nextPulseAt.Clear()));
    }

    protected override void OnDisposed() => _nextPulseAt.Clear();

    private void OnGameTick(GameTickEvent tick)
    {
        foreach (var player in GamePlayers.AllAliveHumans())
        {
            var steamId = player.Controller.SteamID;
            var ability = GetUsable(steamId);
            var amount = ability is null ? 0 : AbilityConfigReader.GetLevelInt(ability, "amount");

            if (ability is null || amount <= 0)
            {
                _nextPulseAt.Remove(steamId);
                continue;
            }

            var interval = Math.Max(
                MinimumInterval,
                AbilityConfigReader.GetLevelDouble(ability, "interval", 1.0));

            // Missing entry, or server time went backwards (map change): restart the pulse.
            if (!_nextPulseAt.TryGetValue(steamId, out var nextAt) ||
                nextAt - tick.ServerTime > interval)
            {
                _nextPulseAt[steamId] = tick.ServerTime + interval;
                continue;
            }

            if (tick.ServerTime < nextAt)
                continue;

            _nextPulseAt[steamId] = tick.ServerTime + interval;
            PlayerHealth.Heal(player.Pawn, amount);
        }
    }
}
