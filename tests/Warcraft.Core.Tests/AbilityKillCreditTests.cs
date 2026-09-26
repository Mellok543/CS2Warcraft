using Warcraft.Core.Game;

namespace Warcraft.Core.Tests;

public sealed class AbilityKillCreditTests
{
    [Fact]
    public void TakeReturnsStoredCreditOnce()
    {
        var now = 10f;
        var tracker = new PendingAbilityKillCredits(() => now);

        tracker.Store(4, 123, "chain_lightning", 2f);

        Assert.True(tracker.TryTake(4, out var credit));
        Assert.Equal((ulong)123, credit.AttackerSteamId);
        Assert.Equal("chain_lightning", credit.AbilityId);
        Assert.False(tracker.TryTake(4, out _));
    }

    [Fact]
    public void ExpiredCreditIsRejectedAndRemoved()
    {
        var now = 10f;
        var tracker = new PendingAbilityKillCredits(() => now);

        tracker.Store(4, 123, "chain_lightning", 2f);
        now = 12.01f;

        Assert.False(tracker.TryTake(4, out _));
        Assert.False(tracker.TryTake(4, out _));
    }

    [Fact]
    public void NewLethalHitReplacesOlderCreditForSameSlot()
    {
        var now = 10f;
        var tracker = new PendingAbilityKillCredits(() => now);

        tracker.Store(4, 123, "reflect_damage", 2f);
        tracker.Store(4, 456, "chain_lightning", 2f);

        Assert.True(tracker.TryTake(4, out var credit));
        Assert.Equal((ulong)456, credit.AttackerSteamId);
        Assert.Equal("chain_lightning", credit.AbilityId);
    }

    [Fact]
    public void PruneRemovesOnlyExpiredCredits()
    {
        var now = 10f;
        var tracker = new PendingAbilityKillCredits(() => now);

        tracker.Store(1, 100, "a", 1f);
        tracker.Store(2, 200, "b", 5f);
        now = 12f;

        Assert.Equal(1, tracker.PruneExpired());
        Assert.False(tracker.TryTake(1, out _));
        Assert.True(tracker.TryTake(2, out _));
    }

    [Fact]
    public void ClearDropsAllCredits()
    {
        var now = 10f;
        var tracker = new PendingAbilityKillCredits(() => now);

        tracker.Store(1, 100, "a", 5f);
        tracker.Store(2, 200, "b", 5f);
        tracker.Clear();

        Assert.False(tracker.TryTake(1, out _));
        Assert.False(tracker.TryTake(2, out _));
    }
}
