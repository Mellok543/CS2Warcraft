using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;
using Warcraft.Core.Persistence;
using Warcraft.Core.Runtime;

namespace Warcraft.Core.Game;

/// <summary>
/// Owns the player connect/disconnect persistence lifecycle.
/// CounterStrikeSharp entities are read only on the game thread; storage I/O
/// happens asynchronously and restored runtime state is marshalled back.
/// </summary>
internal sealed class PlayerLifecycleCoordinator(
    PlayerStateStore players,
    PersistenceCoordinator persistence,
    PersistenceSaveScheduler saveScheduler,
    IGameThreadDispatcher gameThread,
    ILogger logger,
    CancellationToken lifetimeToken) : IDisposable
{
    private bool _started;

    public void Start()
    {
        if (_started)
            return;

        _started = true;
        persistence.FirstProviderRegistered += OnFirstPersistenceProviderRegistered;
    }

    public void AdoptConnectedPlayers()
    {
        foreach (var player in Utilities.GetPlayers())
        {
            if (!IsHuman(player))
                continue;

            players.Upsert(player.SteamID, player.PlayerName);

            if (persistence.HasProvider)
                _ = LoadPlayerAsync(player.SteamID, player.PlayerName);
        }
    }

    public void OnClientPutInServer(int playerSlot)
    {
        var player = Utilities.GetPlayerFromSlot(playerSlot);
        if (!IsHuman(player))
            return;

        var human = player!;
        players.Upsert(human.SteamID, human.PlayerName);

        if (persistence.HasProvider)
            _ = LoadPlayerAsync(human.SteamID, human.PlayerName);
    }

    public void OnClientDisconnect(int playerSlot)
    {
        var player = Utilities.GetPlayerFromSlot(playerSlot);
        if (player is null || player.SteamID == 0)
            return;

        var steamId = player.SteamID;
        var snapshot = players.GetPersistenceSnapshot(steamId);
        players.Remove(steamId);

        if (snapshot is not null)
            _ = saveScheduler.SaveNowAsync(snapshot);
    }

    public void Dispose()
    {
        if (!_started)
            return;

        persistence.FirstProviderRegistered -= OnFirstPersistenceProviderRegistered;
        _started = false;
    }

    private void OnFirstPersistenceProviderRegistered()
    {
        Server.NextFrame(() =>
        {
            if (lifetimeToken.IsCancellationRequested)
                return;

            foreach (var player in players.GetLoadedPlayers())
                _ = LoadPlayerAsync(player.SteamId, player.Name);
        });
    }

    private async Task LoadPlayerAsync(ulong steamId, string currentName)
    {
        if (!persistence.HasProvider || lifetimeToken.IsCancellationRequested)
            return;

        try
        {
            var persisted = await persistence.LoadPlayerAsync(steamId, lifetimeToken);
            if (persisted is null)
                return;

            await gameThread
                .InvokeAsync(() => players.RestoreIfLoaded(persisted, currentName))
                .WaitAsync(lifetimeToken);
        }
        catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to load Warcraft state for {SteamId}.",
                steamId);
        }
    }

    private static bool IsHuman(CCSPlayerController? player)
        => player is { IsValid: true, IsBot: false } && player.SteamID != 0;
}
