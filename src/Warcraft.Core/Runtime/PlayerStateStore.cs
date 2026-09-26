using Warcraft.Api.Players;

namespace Warcraft.Core.Runtime;

internal sealed class PlayerStateStore : IPlayersApi
{
    private readonly Dictionary<ulong, PlayerRuntimeState> _players = [];
    private readonly object _sync = new();

    public PlayerStateSnapshot? Get(ulong steamId)
    {
        lock (_sync)
            return _players.TryGetValue(steamId, out var player) ? player.ToSnapshot() : null;
    }

    public IReadOnlyCollection<PlayerStateSnapshot> GetLoadedPlayers()
    {
        lock (_sync)
            return _players.Values.Select(x => x.ToSnapshot()).ToArray();
    }

    internal PlayerRuntimeState GetRequired(ulong steamId)
    {
        lock (_sync)
        {
            if (!_players.TryGetValue(steamId, out var player))
                throw new InvalidOperationException($"Player {steamId} is not loaded.");

            return player;
        }
    }

    internal PlayerRuntimeState Upsert(ulong steamId, string name)
    {
        lock (_sync)
        {
            if (_players.TryGetValue(steamId, out var existing))
            {
                existing.Name = name;
                return existing;
            }

            var created = new PlayerRuntimeState { SteamId = steamId, Name = name };
            _players[steamId] = created;
            return created;
        }
    }

    internal bool Remove(ulong steamId)
    {
        lock (_sync)
            return _players.Remove(steamId);
    }
}
