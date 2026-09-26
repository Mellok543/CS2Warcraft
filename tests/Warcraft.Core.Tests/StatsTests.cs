using Warcraft.Api.Events;
using Warcraft.Api.Persistence;
using Warcraft.Core.Stats;

namespace Warcraft.Core.Tests;

public sealed class StatsTests
{
    private const ulong Killer = 1;
    private const ulong Victim = 2;

    [Fact]
    public void KillsDeathsHeadshotsAndRoundsAreCounted()
    {
        var core = new TestCore();
        core.Players.Upsert(Killer, "killer");
        core.Players.Upsert(Victim, "victim");
        using var stats = new StatsService(core.Players, core.Events);

        core.Events.Publish(new PlayerKillEvent(Killer, Victim, Headshot: true, TeamKill: false));
        core.Events.Publish(new PlayerDeathEvent(Victim, Killer));
        core.Events.Publish(new PlayerKillEvent(Killer, Victim, Headshot: false, TeamKill: true));
        stats.RecordRoundEnd([new RoundParticipant(Killer, true), new RoundParticipant(Victim, false)]);

        var killer = core.Players.Get(Killer)!.Stats;
        var victim = core.Players.Get(Victim)!.Stats;

        Assert.Equal((1, 1, 0, 1, 1), (killer.Kills, killer.Headshots, killer.Deaths, killer.RoundsPlayed, killer.RoundsWon));
        Assert.Equal((0, 1, 1, 0), (victim.Kills, victim.Deaths, victim.RoundsPlayed, victim.RoundsWon));
    }

    [Fact]
    public void StatsChangesRequestPersistence()
    {
        var core = new TestCore();
        core.Players.Upsert(Victim, "victim");
        using var stats = new StatsService(core.Players, core.Events);
        var reasons = new List<string>();
        core.Events.Subscribe<PlayerStateChangedEvent>(e => reasons.Add(e.Reason));

        core.Events.Publish(new PlayerDeathEvent(Victim, null));

        Assert.Equal(["stats:death"], reasons);
    }

    [Fact]
    public void PlayTimeAccumulatesOnTopOfStoredValue()
    {
        var core = new TestCore();
        core.Players.Upsert(Killer, "player");
        core.Time.Now += TimeSpan.FromSeconds(30);

        core.Players.RestoreIfLoaded(
            new PlayerPersistenceDto
            {
                SteamId = Killer,
                Name = "player",
                Stats = new PlayerStatsPersistenceDto { PlaySeconds = 100, Kills = 7 }
            },
            "player");

        core.Time.Now += TimeSpan.FromSeconds(60);

        var snapshot = core.Players.Get(Killer)!.Stats;
        Assert.Equal(190, snapshot.PlaySeconds);
        Assert.Equal(7, snapshot.Kills);
        Assert.Equal(190, core.Players.GetPersistenceSnapshot(Killer)!.Stats.PlaySeconds);
    }
}
