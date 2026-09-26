using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Warcraft.Api.Persistence;
using Warcraft.Core.Game;
using Warcraft.Core.Runtime;

namespace Warcraft.Core.Persistence;

/// <summary>
/// Debounced and serialized player saves.
/// <list type="bullet">
/// <item>Snapshots are captured on the game thread (runtime state is game-thread affine).</item>
/// <item>Storage I/O runs off the game thread.</item>
/// <item>Saves of one player never overlap, so an older snapshot cannot overwrite a newer one.</item>
/// </list>
/// </summary>
internal sealed class PersistenceSaveScheduler(
    PlayerStateStore players,
    PersistenceCoordinator persistence,
    IGameThreadDispatcher gameThread,
    TimeSpan delay,
    ILogger logger) : IDisposable
{
    private readonly ConcurrentDictionary<ulong, CancellationTokenSource> _pending = new();
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _saveGates = new();
    private readonly CancellationTokenSource _lifetime = new();

    /// <summary>Schedules a debounced save. Call on the game thread.</summary>
    public void Schedule(ulong steamId)
    {
        if (!persistence.HasProvider || _lifetime.IsCancellationRequested)
            return;

        var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);

        _pending.AddOrUpdate(
            steamId,
            tokenSource,
            (_, existing) =>
            {
                TryCancel(existing);
                return tokenSource;
            });

        _ = RunDebouncedSaveAsync(steamId, tokenSource);
    }

    /// <summary>
    /// Saves a snapshot captured by the caller (e.g. on disconnect, right before
    /// the runtime state is removed). Pending debounced saves are superseded.
    /// </summary>
    public async Task SaveNowAsync(PlayerPersistenceDto snapshot)
    {
        if (_pending.TryRemove(snapshot.SteamId, out var pending))
            TryCancel(pending);

        try
        {
            await SaveSerializedAsync(
                snapshot.SteamId,
                () => Task.FromResult<PlayerPersistenceDto?>(snapshot),
                _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to save Warcraft state for {SteamId}.", snapshot.SteamId);
        }
    }

    public void Dispose()
    {
        _lifetime.Cancel();

        foreach (var tokenSource in _pending.Values)
            TryCancel(tokenSource);

        _pending.Clear();
    }

    private async Task RunDebouncedSaveAsync(ulong steamId, CancellationTokenSource tokenSource)
    {
        try
        {
            await Task.Delay(delay, tokenSource.Token);

            await SaveSerializedAsync(
                steamId,
                () => gameThread.InvokeAsync(() => players.GetPersistenceSnapshot(steamId)),
                tokenSource.Token);
        }
        catch (OperationCanceledException) when (tokenSource.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Debounced Warcraft save failed for {SteamId}.", steamId);
        }
        finally
        {
            _pending.TryRemove(new KeyValuePair<ulong, CancellationTokenSource>(steamId, tokenSource));
            tokenSource.Dispose();
        }
    }

    private async Task SaveSerializedAsync(
        ulong steamId,
        Func<Task<PlayerPersistenceDto?>> capture,
        CancellationToken cancellationToken)
    {
        if (!persistence.HasProvider)
            return;

        var gate = _saveGates.GetOrAdd(steamId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);

        try
        {
            var snapshot = await capture().WaitAsync(cancellationToken);
            if (snapshot is null)
                return;

            // Continue on the thread pool: never run storage I/O on the game thread.
            await Task.Yield();
            await persistence.SavePlayerAsync(snapshot, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>A finished debounced save disposes its own source; cancelling it then is a no-op.</summary>
    private static void TryCancel(CancellationTokenSource tokenSource)
    {
        try
        {
            tokenSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
