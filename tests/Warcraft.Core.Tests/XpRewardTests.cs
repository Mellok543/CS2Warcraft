using Warcraft.Api.Events;
using Warcraft.Api.Modifiers;
using Warcraft.Core.Progression;

namespace Warcraft.Core.Tests;

public sealed class XpRewardTests
{
    private const ulong Player = 11;
    private const ulong Other = 12;

    private sealed class FixedModifiers(PlayerModifiers value) : IPlayerModifierProvider
    {
        public string ProviderId => "test";
        public PlayerModifiers GetModifiers(ulong steamId) => value;
    }

    private static (TestCore Core, XpRewardService Rewards) Create()
    {
        var core = new TestCore();
        core.Players.Upsert(Player, "p");
        core.Players.Upsert(Other, "o");
        var config = new CoreConfig
        {
            KillXp = 25,
            HeadshotBonusXp = 10,
            KnifeKillBonusXp = 30,
            DeathPenaltyXp = 8,
            RoundLossPenaltyXp = 12,
            AssistXp = 7,
            RoundWinXp = 15,
            BombPlantXp = 20,
            BombDefuseXp = 30
        };
        return (core, new XpRewardService(core.Players, core.Progress, core.Events, config));
    }

    [Fact]
    public void GameplayEventsGrantConfiguredXp()
    {
        var (core, rewards) = Create();
        using var _ = rewards;

        core.Events.Publish(new PlayerKillEvent(Player, Other, Headshot: true, TeamKill: false));  // 35
        core.Events.Publish(new PlayerKillEvent(Player, Other, Headshot: false, TeamKill: true));  // 0
        core.Events.Publish(new PlayerAssistEvent(Player, Other, false));                          // 7
        core.Events.Publish(new PlayerRoundResultEvent(Player, true));                             // 15
        core.Events.Publish(new PlayerRoundResultEvent(Player, false));                            // -12
        core.Events.Publish(new BombPlantedEvent(Player));                                         // 20
        core.Events.Publish(new BombDefusedEvent(Player));                                         // 30

        Assert.Equal(95, core.Players.Get(Player)!.GlobalXp);
    }

    [Fact]
    public void KillComboAddsConfiguredBonusXp()
    {
        var (core, rewards) = Create();
        using var _ = rewards;

        core.Events.Publish(new PlayerKillEvent(Player, Other, Headshot: false, TeamKill: false));
        core.Events.Publish(new PlayerKillEvent(Player, Other, Headshot: false, TeamKill: false));

        Assert.Equal(60, core.Players.Get(Player)!.GlobalXp);
    }

    [Fact]
    public void KnifeKillAddsBonusAndPenaltiesRemoveXp()
    {
        var (core, rewards) = Create();
        using var _ = rewards;

        core.Progress.AddXp(Player, 100, "seed");
        core.Events.Publish(new PlayerKillEvent(Player, Other, Headshot: false, TeamKill: false, Weapon: "weapon_knife"));
        core.Events.Publish(new PlayerDeathEvent(Player, Other));
        core.Events.Publish(new PlayerRoundResultEvent(Player, false));

        Assert.Equal(135, core.Players.Get(Player)!.GlobalXp);
    }

    [Fact]
    public void ModifierMultiplierIsAppliedByCore()
    {
        var (core, rewards) = Create();
        using var _ = rewards;
        core.Modifiers.RegisterProvider(new FixedModifiers(new PlayerModifiers(XpMultiplier: 1.5)));

        var gained = new List<PlayerXpGainedEvent>();
        core.Events.Subscribe<PlayerXpGainedEvent>(gained.Add);

        core.Events.Publish(new PlayerKillEvent(Player, Other, Headshot: false, TeamKill: false));

        Assert.Equal(38, core.Players.Get(Player)!.GlobalXp);
        Assert.Equal(38, Assert.Single(gained).Amount);
    }
}
