using Warcraft.Api.Persistence;
using Warcraft.Api.Players;

namespace Warcraft.Core.Runtime;

internal sealed class PlayerStateStore : IPlayersApi
{
    private readonly Dictionary<ulong, PlayerRuntimeState> _players = [];
    private readonly object _sync = new();

    public PlayerStateSnapshot? Get(ulong steamId)
    {
        lock (_sync)
            return _players.TryGetValue(steamId, out var player)
                ? player.ToSnapshot()
                : null;
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

    internal PlayerRuntimeState? TryGetRuntime(ulong steamId)
    {
        lock (_sync)
            return _players.GetValueOrDefault(steamId);
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

            var created = new PlayerRuntimeState
            {
                SteamId = steamId,
                Name = name
            };

            _players[steamId] = created;
            return created;
        }
    }

    internal bool RestoreIfLoaded(PlayerPersistenceDto persisted, string currentName)
    {
        lock (_sync)
        {
            if (!_players.ContainsKey(persisted.SteamId))
                return false;

            var player = new PlayerRuntimeState
            {
                SteamId = persisted.SteamId,
                Name = currentName,
                GlobalXp = persisted.GlobalXp,
                ActiveRaceId = persisted.ActiveRaceId
            };

            foreach (var race in persisted.Races)
            {
                var runtime = new RaceProgressRuntime
                {
                    RaceId = race.RaceId,
                    Level = race.Level,
                    Xp = race.Xp,
                    SkillPoints = race.SkillPoints
                };

                foreach (var ability in race.AbilityLevels)
                    runtime.AbilityLevels[ability.Key] = ability.Value;

                player.Races[race.RaceId] = runtime;
            }

            _players[persisted.SteamId] = player;
            return true;
        }
    }

    internal PlayerPersistenceDto? GetPersistenceSnapshot(ulong steamId)
    {
        lock (_sync)
        {
            return _players.TryGetValue(steamId, out var player)
                ? player.ToPersistence()
                : null;
        }
    }

    internal IReadOnlyCollection<PlayerPersistenceDto> GetPersistenceSnapshots()
    {
        lock (_sync)
            return _players.Values.Select(x => x.ToPersistence()).ToArray();
    }

    internal bool Remove(ulong steamId)
    {
        lock (_sync)
            return _players.Remove(steamId);
    }
}
