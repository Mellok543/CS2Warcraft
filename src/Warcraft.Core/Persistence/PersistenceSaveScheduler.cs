using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Warcraft.Api.Persistence;
using Warcraft.Core.Runtime;

namespace Warcraft.Core.Persistence;

internal sealed class PersistenceSaveScheduler(
    PlayerStateStore players,
    PersistenceCoordinator persistence,
    TimeSpan delay,
    ILogger logger) : IDisposable
{
    private readonly ConcurrentDictionary<ulong, CancellationTokenSource> _pending = new();
    private readonly CancellationTokenSource _lifetime = new();

    public void Schedule(ulong steamId)
    {
        if (!persistence.HasProvider)
            return;

        var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);

        var previous = _pending.AddOrUpdate(
            steamId,
            tokenSource,
            (_, existing) =>
            {
                existing.Cancel();
                existing.Dispose();
                return tokenSource;
            });

        if (!ReferenceEquals(previous, tokenSource))
            return;

        _ = RunSaveAsync(steamId, tokenSource);
    }

    public async ValueTask FlushAsync(ulong steamId)
    {
        if (_pending.TryRemove(steamId, out var pending))
        {
            pending.Cancel();
            pending.Dispose();
        }

        if (!persistence.HasProvider)
            return;

        var snapshot = players.GetPersistenceSnapshot(steamId);
        if (snapshot is null)
            return;

        await persistence.SavePlayerAsync(snapshot, _lifetime.Token);
    }

    private async Task RunSaveAsync(
        ulong steamId,
        CancellationTokenSource tokenSource)
    {
        try
        {
            await Task.Delay(delay, tokenSource.Token);

            if (!persistence.HasProvider)
                return;

            var snapshot = players.GetPersistenceSnapshot(steamId);
            if (snapshot is not null)
                await persistence.SavePlayerAsync(snapshot, tokenSource.Token);
        }
        catch (OperationCanceledException) when (tokenSource.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Debounced Warcraft save failed for {SteamId}.",
                steamId);
        }
        finally
        {
            if (_pending.TryGetValue(steamId, out var current) &&
                ReferenceEquals(current, tokenSource))
            {
                _pending.TryRemove(steamId, out _);
            }

            tokenSource.Dispose();
        }
    }

    public void Dispose()
    {
        _lifetime.Cancel();

        foreach (var tokenSource in _pending.Values)
        {
            tokenSource.Cancel();
            tokenSource.Dispose();
        }

        _pending.Clear();
        _lifetime.Dispose();
    }
}
