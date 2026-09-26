using MySqlConnector;
using Warcraft.Api.Persistence;

namespace Warcraft.Database;

internal sealed class MySqlStorageProvider : IWarcraftStorageProvider
{
    public const string Name = "warcraft.mysql";

    private readonly string _connectionString;

    public MySqlStorageProvider(DatabaseConfig config)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = config.Host,
            Port = config.Port,
            Database = config.Database,
            UserID = config.User,
            Password = config.Password,
            MinimumPoolSize = config.MinimumPoolSize,
            MaximumPoolSize = config.MaximumPoolSize,
            ConnectionTimeout = config.ConnectionTimeoutSeconds,
            Pooling = true,
            CharacterSet = "utf8mb4"
        };

        _connectionString = builder.ConnectionString;
    }

    public string ProviderName => Name;

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var commands = new[]
        {
            """
            CREATE TABLE IF NOT EXISTS wc_players (
                steam_id BIGINT UNSIGNED NOT NULL,
                name VARCHAR(128) NOT NULL,
                global_xp BIGINT NOT NULL DEFAULT 0,
                active_race_id VARCHAR(64) NULL,
                created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                PRIMARY KEY (steam_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """,
            """
            CREATE TABLE IF NOT EXISTS wc_race_progress (
                steam_id BIGINT UNSIGNED NOT NULL,
                race_id VARCHAR(64) NOT NULL,
                level INT NOT NULL DEFAULT 1,
                xp BIGINT NOT NULL DEFAULT 0,
                skill_points INT NOT NULL DEFAULT 0,
                PRIMARY KEY (steam_id, race_id),
                CONSTRAINT fk_wc_race_progress_player
                    FOREIGN KEY (steam_id) REFERENCES wc_players(steam_id)
                    ON DELETE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """,
            """
            CREATE TABLE IF NOT EXISTS wc_ability_progress (
                steam_id BIGINT UNSIGNED NOT NULL,
                race_id VARCHAR(64) NOT NULL,
                ability_id VARCHAR(64) NOT NULL,
                level INT NOT NULL DEFAULT 0,
                PRIMARY KEY (steam_id, race_id, ability_id),
                CONSTRAINT fk_wc_ability_progress_race
                    FOREIGN KEY (steam_id, race_id)
                    REFERENCES wc_race_progress(steam_id, race_id)
                    ON DELETE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """,
            """
            CREATE TABLE IF NOT EXISTS wc_player_stats (
                steam_id BIGINT UNSIGNED NOT NULL,
                kills BIGINT NOT NULL DEFAULT 0,
                deaths BIGINT NOT NULL DEFAULT 0,
                headshots BIGINT NOT NULL DEFAULT 0,
                rounds_played BIGINT NOT NULL DEFAULT 0,
                rounds_won BIGINT NOT NULL DEFAULT 0,
                play_seconds BIGINT NOT NULL DEFAULT 0,
                PRIMARY KEY (steam_id),
                CONSTRAINT fk_wc_player_stats_player
                    FOREIGN KEY (steam_id) REFERENCES wc_players(steam_id)
                    ON DELETE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """
        };

        foreach (var sql in commands)
        {
            await using var command = new MySqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await EnsureColumnAsync(
            connection,
            "wc_player_stats",
            "rounds_played",
            "ALTER TABLE wc_player_stats ADD COLUMN rounds_played BIGINT NOT NULL DEFAULT 0 AFTER headshots;",
            cancellationToken);
    }

    /// <summary>Additive migration for databases created by earlier versions.</summary>
    private static async Task EnsureColumnAsync(
        MySqlConnection connection,
        string table,
        string column,
        string alterSql,
        CancellationToken cancellationToken)
    {
        await using (var exists = new MySqlCommand(
            """
            SELECT COUNT(*)
            FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = @table
              AND COLUMN_NAME = @column;
            """,
            connection))
        {
            exists.Parameters.AddWithValue("@table", table);
            exists.Parameters.AddWithValue("@column", column);

            var count = Convert.ToInt64(await exists.ExecuteScalarAsync(cancellationToken));
            if (count > 0)
                return;
        }

        await using var alter = new MySqlCommand(alterSql, connection);
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<PlayerPersistenceDto?> LoadPlayerAsync(
        ulong steamId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        string name;
        long globalXp;
        string? activeRaceId;

        await using (var command = new MySqlCommand(
            """
            SELECT name, global_xp, active_race_id
            FROM wc_players
            WHERE steam_id = @steamId;
            """,
            connection))
        {
            command.Parameters.AddWithValue("@steamId", steamId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return null;

            name = reader.GetString(0);
            globalXp = reader.GetInt64(1);
            activeRaceId = reader.IsDBNull(2) ? null : reader.GetString(2);
        }

        var races = new Dictionary<string, MutableRaceProgress>(
            StringComparer.OrdinalIgnoreCase);

        await using (var command = new MySqlCommand(
            """
            SELECT race_id, level, xp, skill_points
            FROM wc_race_progress
            WHERE steam_id = @steamId;
            """,
            connection))
        {
            command.Parameters.AddWithValue("@steamId", steamId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var raceId = reader.GetString(0);
                races[raceId] = new MutableRaceProgress
                {
                    RaceId = raceId,
                    Level = reader.GetInt32(1),
                    Xp = reader.GetInt64(2),
                    SkillPoints = reader.GetInt32(3)
                };
            }
        }

        await using (var command = new MySqlCommand(
            """
            SELECT race_id, ability_id, level
            FROM wc_ability_progress
            WHERE steam_id = @steamId;
            """,
            connection))
        {
            command.Parameters.AddWithValue("@steamId", steamId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var raceId = reader.GetString(0);
                if (!races.TryGetValue(raceId, out var race))
                    continue;

                race.AbilityLevels[reader.GetString(1)] = reader.GetInt32(2);
            }
        }

        var stats = new PlayerStatsPersistenceDto();

        await using (var command = new MySqlCommand(
            """
            SELECT kills, deaths, headshots, rounds_played, rounds_won, play_seconds
            FROM wc_player_stats
            WHERE steam_id = @steamId;
            """,
            connection))
        {
            command.Parameters.AddWithValue("@steamId", steamId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                stats = new PlayerStatsPersistenceDto
                {
                    Kills = reader.GetInt64(0),
                    Deaths = reader.GetInt64(1),
                    Headshots = reader.GetInt64(2),
                    RoundsPlayed = reader.GetInt64(3),
                    RoundsWon = reader.GetInt64(4),
                    PlaySeconds = reader.GetInt64(5)
                };
            }
        }

        return new PlayerPersistenceDto
        {
            SteamId = steamId,
            Name = name,
            GlobalXp = globalXp,
            ActiveRaceId = activeRaceId,
            Stats = stats,
            Races = races.Values
                .Select(x => new RaceProgressPersistenceDto
                {
                    RaceId = x.RaceId,
                    Level = x.Level,
                    Xp = x.Xp,
                    SkillPoints = x.SkillPoints,
                    AbilityLevels = x.AbilityLevels
                })
                .ToArray()
        };
    }

    public async ValueTask SavePlayerAsync(
        PlayerPersistenceDto player,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await UpsertPlayerAsync(connection, transaction, player, cancellationToken);
            await ReplaceProgressAsync(connection, transaction, player, cancellationToken);
            await UpsertStatsAsync(connection, transaction, player, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task UpsertPlayerAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        PlayerPersistenceDto player,
        CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand(
            """
            INSERT INTO wc_players (steam_id, name, global_xp, active_race_id)
            VALUES (@steamId, @name, @globalXp, @activeRaceId)
            ON DUPLICATE KEY UPDATE
                name = VALUES(name),
                global_xp = VALUES(global_xp),
                active_race_id = VALUES(active_race_id);
            """,
            connection,
            transaction);

        command.Parameters.AddWithValue("@steamId", player.SteamId);
        command.Parameters.AddWithValue("@name", player.Name);
        command.Parameters.AddWithValue("@globalXp", player.GlobalXp);
        command.Parameters.AddWithValue(
            "@activeRaceId",
            (object?)player.ActiveRaceId ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertStatsAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        PlayerPersistenceDto player,
        CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand(
            """
            INSERT INTO wc_player_stats
                (steam_id, kills, deaths, headshots, rounds_played, rounds_won, play_seconds)
            VALUES
                (@steamId, @kills, @deaths, @headshots, @roundsPlayed, @roundsWon, @playSeconds)
            ON DUPLICATE KEY UPDATE
                kills = VALUES(kills),
                deaths = VALUES(deaths),
                headshots = VALUES(headshots),
                rounds_played = VALUES(rounds_played),
                rounds_won = VALUES(rounds_won),
                play_seconds = VALUES(play_seconds);
            """,
            connection,
            transaction);

        var stats = player.Stats;
        command.Parameters.AddWithValue("@steamId", player.SteamId);
        command.Parameters.AddWithValue("@kills", stats.Kills);
        command.Parameters.AddWithValue("@deaths", stats.Deaths);
        command.Parameters.AddWithValue("@headshots", stats.Headshots);
        command.Parameters.AddWithValue("@roundsPlayed", stats.RoundsPlayed);
        command.Parameters.AddWithValue("@roundsWon", stats.RoundsWon);
        command.Parameters.AddWithValue("@playSeconds", stats.PlaySeconds);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ReplaceProgressAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        PlayerPersistenceDto player,
        CancellationToken cancellationToken)
    {
        await using (var delete = new MySqlCommand(
            "DELETE FROM wc_race_progress WHERE steam_id = @steamId;",
            connection,
            transaction))
        {
            delete.Parameters.AddWithValue("@steamId", player.SteamId);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var race in player.Races)
        {
            await using (var insertRace = new MySqlCommand(
                """
                INSERT INTO wc_race_progress
                    (steam_id, race_id, level, xp, skill_points)
                VALUES
                    (@steamId, @raceId, @level, @xp, @skillPoints);
                """,
                connection,
                transaction))
            {
                insertRace.Parameters.AddWithValue("@steamId", player.SteamId);
                insertRace.Parameters.AddWithValue("@raceId", race.RaceId);
                insertRace.Parameters.AddWithValue("@level", race.Level);
                insertRace.Parameters.AddWithValue("@xp", race.Xp);
                insertRace.Parameters.AddWithValue("@skillPoints", race.SkillPoints);
                await insertRace.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var ability in race.AbilityLevels)
            {
                await using var insertAbility = new MySqlCommand(
                    """
                    INSERT INTO wc_ability_progress
                        (steam_id, race_id, ability_id, level)
                    VALUES
                        (@steamId, @raceId, @abilityId, @level);
                    """,
                    connection,
                    transaction);

                insertAbility.Parameters.AddWithValue("@steamId", player.SteamId);
                insertAbility.Parameters.AddWithValue("@raceId", race.RaceId);
                insertAbility.Parameters.AddWithValue("@abilityId", ability.Key);
                insertAbility.Parameters.AddWithValue("@level", ability.Value);
                await insertAbility.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }

    private sealed class MutableRaceProgress
    {
        public required string RaceId { get; init; }
        public int Level { get; init; }
        public long Xp { get; init; }
        public int SkillPoints { get; init; }

        public Dictionary<string, int> AbilityLevels { get; } =
            new(StringComparer.OrdinalIgnoreCase);
    }
}
