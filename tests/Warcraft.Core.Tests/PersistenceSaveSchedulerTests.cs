using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Warcraft.Api.Persistence;
using Warcraft.Core.Game;
using Warcraft.Core.Persistence;

namespace Warcraft.Core.Tests;

public sealed class PersistenceSaveSchedulerTests
{
    private const ulong Player = 42;

    private sealed class InlineDispatcher : IGameThreadDispatcher
    {
        public Task<T> InvokeAsync<T>(Func<T> work) => Task.FromResult(work());
    }

    private sealed class RecordingProvider : IWarcraftStorageProvider
    {
        private int _concurrent;

        public ConcurrentQueue<PlayerPersistenceDto> Saved { get; } = new();
        public bool OverlapDetected { get; private set; }
        public TimeSpan SaveDuration { get; init; }

        public string ProviderName => "test";

        public ValueTask<PlayerPersistenceDto?> LoadPlayerAsync(ulong steamId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<PlayerPersistenceDto?>(null);

        public async ValueTask SavePlayerAsync(PlayerPersistenceDto player, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _concurrent) > 1)
                OverlapDetected = true;

            try
            {
                await Task.Delay(SaveDuration, cancellationToken);
                Saved.Enqueue(player);
            }
            finally
            {
                Interlocked.Decrement(ref _concurrent);
            }
        }
    }

    private static (TestCore Core, RecordingProvider Provider, PersistenceSaveScheduler Scheduler) Create(
        TimeSpan delay,
        TimeSpan saveDuration = default)
    {
        var core = new TestCore();
        core.Players.Upsert(Player, "player");
        core.Players.MarkLoaded(Player);

        var persistence = new PersistenceCoordinator();
        var provider = new RecordingProvider { SaveDuration = saveDuration };
        persistence.RegisterProvider(provider);

        var scheduler = new PersistenceSaveScheduler(
            core.Players,
            persistence,
            new InlineDispatcher(),
            delay,
            NullLogger.Instance);

        return (core, provider, scheduler);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        Assert.True(condition());
    }

    [Fact]
    public async Task BurstOfChangesIsSavedOnceWithLatestState()
    {
        var (core, provider, scheduler) = Create(TimeSpan.FromMilliseconds(100));
        using var _ = scheduler;

        for (var xp = 1; xp <= 5; xp++)
        {
            core.Players.GetRequired(Player).GlobalXp = xp;
            scheduler.Schedule(Player);
        }

        await WaitUntil(() => !provider.Saved.IsEmpty);
        await Task.Delay(250);

        var saved = Assert.Single(provider.Saved);
        Assert.Equal(5, saved.GlobalXp);
    }

    [Fact]
    public async Task DisconnectSaveSupersedesPendingDebouncedSave()
    {
        var (core, provider, scheduler) = Create(TimeSpan.FromMilliseconds(100));
        using var _ = scheduler;

        core.Players.GetRequired(Player).GlobalXp = 10;
        scheduler.Schedule(Player);

        var snapshot = core.Players.GetPersistenceSnapshot(Player)!;
        core.Players.Remove(Player);
        await scheduler.SaveNowAsync(snapshot);
        await Task.Delay(250);

        var saved = Assert.Single(provider.Saved);
        Assert.Equal(10, saved.GlobalXp);
    }

    [Fact]
    public async Task SavesOfOnePlayerNeverOverlap()
    {
        var (core, provider, scheduler) = Create(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(80));
        using var _ = scheduler;

        scheduler.Schedule(Player);
        await Task.Delay(30);

        core.Players.GetRequired(Player).GlobalXp = 99;
        await scheduler.SaveNowAsync(core.Players.GetPersistenceSnapshot(Player)!);

        Assert.False(provider.OverlapDetected);
        Assert.Equal(99, provider.Saved.Last().GlobalXp);
    }

    [Fact]
    public async Task FlushSupersedesPendingSavesAndWritesLatestSnapshotOnce()
    {
        var (core, provider, scheduler) = Create(TimeSpan.FromSeconds(30));
        using var _ = scheduler;

        core.Players.GetRequired(Player).GlobalXp = 5;
        scheduler.Schedule(Player);
        core.Players.GetRequired(Player).GlobalXp = 7;

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await scheduler.FlushAsync(core.Players.GetPersistenceSnapshots(), timeout.Token);
        await Task.Delay(100);

        var saved = Assert.Single(provider.Saved);
        Assert.Equal(7, saved.GlobalXp);
    }

    [Fact]
    public async Task FlushWaitsForInFlightSaveOfTheSamePlayer()
    {
        var (core, provider, scheduler) = Create(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(100));
        using var _ = scheduler;

        scheduler.Schedule(Player);
        await Task.Delay(40);
        core.Players.GetRequired(Player).GlobalXp = 42;

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await scheduler.FlushAsync(core.Players.GetPersistenceSnapshots(), timeout.Token);

        Assert.False(provider.OverlapDetected);
        Assert.Equal(42, provider.Saved.Last().GlobalXp);
    }
}
