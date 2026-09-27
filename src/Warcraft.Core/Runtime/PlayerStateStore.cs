using Warcraft.Api.Persistence;
using Warcraft.Api.Players;

namespace Warcraft.Core.Runtime;

/// <summary>
/// Owner of all <see cref="PlayerRuntimeState"/> instances. The dictionary is
/// lock-protected; the state objects themselves are game-thread affine and are
/// only mutated and snapshotted on the game thread.
/// </summary>
internal sealed class PlayerStateStore(TimeProvider? time = null) : IPlayersApi
{
    private readonly Dictionary<ulong, PlayerRuntimeState> _players = [];
    private readonly object _sync = new();
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public PlayerStateSnapshot? Get(ulong steamId)
    {
        var now = _time.GetUtcNow();

        lock (_sync)
            return _players.TryGetValue(steamId, out var player)
                ? player.ToSnapshot(now)
                : null;
    }

    public IReadOnlyCollection<PlayerStateSnapshot> GetLoadedPlayers()
    {
        var now = _time.GetUtcNow();

        lock (_sync)
            return _players.Values.Select(x => x.ToSnapshot(now)).ToArray();
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
                Name = name,
                Stats = { SessionStartedAt = _time.GetUtcNow() }
            };

            _players[steamId] = created;
            return created;
        }
    }

    internal bool RestoreIfLoaded(PlayerPersistenceDto persisted, string currentName)
    {
        lock (_sync)
        {
            if (!_players.TryGetValue(persisted.SteamId, out var current))
                return false;

            var player = new PlayerRuntimeState
            {
                SteamId = persisted.SteamId,
                Name = currentName,
                GlobalXp = persisted.GlobalXp,
                ActiveRaceId = persisted.ActiveRaceId,
                Stats = PlayerStatsRuntime.FromPersistence(
                    persisted.Stats,
                    current.Stats.SessionStartedAt)
            };

            foreach (var achievement in persisted.Achievements)
            {
                player.Achievements[achievement.AchievementId] = new AchievementProgressRuntime
                {
                    Progress = achievement.Progress,
                    Unlocked = achievement.Unlocked,
                    UnlockedAt = achievement.UnlockedAt
                };
            }

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
        var now = _time.GetUtcNow();

        lock (_sync)
        {
            return _players.TryGetValue(steamId, out var player)
                ? player.ToPersistence(now)
                : null;
        }
    }

    internal IReadOnlyCollection<PlayerPersistenceDto> GetPersistenceSnapshots()
    {
        var now = _time.GetUtcNow();

        lock (_sync)
            return _players.Values.Select(x => x.ToPersistence(now)).ToArray();
    }

    internal bool Remove(ulong steamId)
    {
        lock (_sync)
            return _players.Remove(steamId);
    }
}
