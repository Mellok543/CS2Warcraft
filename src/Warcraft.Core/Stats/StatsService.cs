using Warcraft.Api.Events;
using Warcraft.Core.Runtime;

namespace Warcraft.Core.Stats;

internal readonly record struct RoundParticipant(ulong SteamId, bool Won);

/// <summary>
/// Central statistics updater. Consumes Core events (never engine events) and
/// mutates <see cref="PlayerStatsRuntime"/>; persistence is scheduled through
/// the regular <see cref="PlayerStateChangedEvent"/> flow.
/// </summary>
internal sealed class StatsService : IDisposable
{
    private readonly PlayerStateStore _players;
    private readonly IWarcraftEventBus _events;
    private readonly IDisposable[] _subscriptions;

    public StatsService(PlayerStateStore players, IWarcraftEventBus events)
    {
        _players = players;
        _events = events;
        _subscriptions =
        [
            events.Subscribe<PlayerKillEvent>(OnKill),
            events.Subscribe<PlayerDeathEvent>(OnDeath)
        ];
    }

    public void RecordRoundEnd(IReadOnlyCollection<RoundParticipant> participants)
    {
        foreach (var participant in participants)
        {
            Update(participant.SteamId, "stats:round", stats =>
            {
                stats.RoundsPlayed++;
                if (participant.Won)
                    stats.RoundsWon++;
            });
        }
    }

    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
            subscription.Dispose();
    }

    private void OnKill(PlayerKillEvent kill)
    {
        if (kill.TeamKill)
            return;

        Update(kill.KillerSteamId, "stats:kill", stats =>
        {
            stats.Kills++;
            if (kill.Headshot)
                stats.Headshots++;
        });
    }

    private void OnDeath(PlayerDeathEvent death)
        => Update(death.SteamId, "stats:death", stats => stats.Deaths++);

    private void Update(ulong steamId, string reason, Action<PlayerStatsRuntime> mutate)
    {
        var player = _players.TryGetRuntime(steamId);
        if (player is null)
            return;

        mutate(player.Stats);
        _events.Publish(new PlayerStateChangedEvent(steamId, reason));
    }
}
