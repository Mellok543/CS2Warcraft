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
                achievement_currency BIGINT NOT NULL DEFAULT 0,
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
,
            """
            CREATE TABLE IF NOT EXISTS wc_achievement_progress (
                steam_id BIGINT UNSIGNED NOT NULL,
                achievement_id VARCHAR(96) NOT NULL,
                progress BIGINT NOT NULL DEFAULT 0,
                unlocked TINYINT(1) NOT NULL DEFAULT 0,
                unlocked_at DATETIME(6) NULL,
                PRIMARY KEY (steam_id, achievement_id),
                CONSTRAINT fk_wc_achievement_progress_player
                    FOREIGN KEY (steam_id) REFERENCES wc_players(steam_id)
                    ON DELETE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """,
            """
            CREATE TABLE IF NOT EXISTS wc_cosmetic_inventory (
                steam_id BIGINT UNSIGNED NOT NULL,
                cosmetic_id VARCHAR(96) NOT NULL,
                PRIMARY KEY (steam_id, cosmetic_id),
                CONSTRAINT fk_wc_cosmetic_inventory_player
                    FOREIGN KEY (steam_id) REFERENCES wc_players(steam_id)
                    ON DELETE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """,
            """
            CREATE TABLE IF NOT EXISTS wc_cosmetic_equipped (
                steam_id BIGINT UNSIGNED NOT NULL,
                slot_id VARCHAR(32) NOT NULL,
                cosmetic_id VARCHAR(96) NOT NULL,
                PRIMARY KEY (steam_id, slot_id),
                CONSTRAINT fk_wc_cosmetic_equipped_player
                    FOREIGN KEY (steam_id) REFERENCES wc_players(steam_id)
                    ON DELETE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """,
            """
            CREATE TABLE IF NOT EXISTS wc_admins (
                steam_id BIGINT UNSIGNED NOT NULL,
                name VARCHAR(128) NOT NULL DEFAULT '',
                flags VARCHAR(32) NOT NULL,
                immunity INT NOT NULL DEFAULT 0,
                created_at DATETIME(6) NOT NULL,
                expires_at DATETIME(6) NULL,
                PRIMARY KEY (steam_id)
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
            "wc_players",
            "achievement_currency",
            "ALTER TABLE wc_players ADD COLUMN achievement_currency BIGINT NOT NULL DEFAULT 0 AFTER global_xp;",
            cancellationToken);

        await EnsureColumnAsync(
            connection,
            "wc_admins",
            "name",
            "ALTER TABLE wc_admins ADD COLUMN name VARCHAR(128) NOT NULL DEFAULT '' AFTER steam_id;",
            cancellationToken);

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
        long achievementCurrency;
        string? activeRaceId;

        await using (var command = new MySqlCommand(
            """
            SELECT name, global_xp, achievement_currency, active_race_id
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
            achievementCurrency = reader.GetInt64(2);
            activeRaceId = reader.IsDBNull(3) ? null : reader.GetString(3);
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

        var achievements = new List<AchievementProgressPersistenceDto>();

        await using (var command = new MySqlCommand(
            """
            SELECT achievement_id, progress, unlocked, unlocked_at
            FROM wc_achievement_progress
            WHERE steam_id = @steamId;
            """,
            connection))
        {
            command.Parameters.AddWithValue("@steamId", steamId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                achievements.Add(new AchievementProgressPersistenceDto
                {
                    AchievementId = reader.GetString(0),
                    Progress = reader.GetInt64(1),
                    Unlocked = reader.GetBoolean(2),
                    UnlockedAt = reader.IsDBNull(3)
                        ? null
                        : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc))
                });
            }
        }

        var ownedCosmetics = new List<string>();
        await using (var command = new MySqlCommand(
            """
            SELECT cosmetic_id
            FROM wc_cosmetic_inventory
            WHERE steam_id = @steamId;
            """,
            connection))
        {
            command.Parameters.AddWithValue("@steamId", steamId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                ownedCosmetics.Add(reader.GetString(0));
        }

        var equippedCosmetics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using (var command = new MySqlCommand(
            """
            SELECT slot_id, cosmetic_id
            FROM wc_cosmetic_equipped
            WHERE steam_id = @steamId;
            """,
            connection))
        {
            command.Parameters.AddWithValue("@steamId", steamId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                equippedCosmetics[reader.GetString(0)] = reader.GetString(1);
        }

        return new PlayerPersistenceDto
        {
            SteamId = steamId,
            Name = name,
            GlobalXp = globalXp,
            AchievementCurrency = achievementCurrency,
            ActiveRaceId = activeRaceId,
            Stats = stats,
            Achievements = achievements,
            OwnedCosmetics = ownedCosmetics,
            EquippedCosmetics = equippedCosmetics,
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

    public async ValueTask<IReadOnlyList<AdminPersistenceEntry>> LoadAdminsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new MySqlCommand(
            """
            SELECT steam_id, name, flags, immunity, created_at, expires_at
            FROM wc_admins
            WHERE expires_at IS NULL OR expires_at > UTC_TIMESTAMP(6)
            ORDER BY immunity DESC, steam_id ASC;
            """,
            connection);

        var result = new List<AdminPersistenceEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new AdminPersistenceEntry(
                reader.GetFieldValue<ulong>(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(4), DateTimeKind.Utc)),
                reader.IsDBNull(5)
                    ? null
                    : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc))));
        }

        return result;
    }

    public async ValueTask UpsertAdminAsync(
        AdminPersistenceEntry admin,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new MySqlCommand(
            """
            INSERT INTO wc_admins (steam_id, name, flags, immunity, created_at, expires_at)
            VALUES (@steamId, @name, @flags, @immunity, @createdAt, @expiresAt)
            ON DUPLICATE KEY UPDATE
                name = VALUES(name),
                flags = VALUES(flags),
                immunity = VALUES(immunity),
                expires_at = VALUES(expires_at);
            """,
            connection);

        command.Parameters.AddWithValue("@steamId", admin.SteamId);
        command.Parameters.AddWithValue("@name", admin.Name);
        command.Parameters.AddWithValue("@flags", admin.Flags);
        command.Parameters.AddWithValue("@immunity", admin.Immunity);
        command.Parameters.AddWithValue("@createdAt", admin.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("@expiresAt", admin.ExpiresAt?.UtcDateTime ?? (object)DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask DeleteAdminAsync(
        ulong steamId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new MySqlCommand(
            "DELETE FROM wc_admins WHERE steam_id = @steamId;",
            connection);
        command.Parameters.AddWithValue("@steamId", steamId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<IReadOnlyList<PlayerLeaderboardEntry>> LoadLevelLeaderboardAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new MySqlCommand(
            """
            SELECT
                p.steam_id,
                p.name,
                p.global_xp,
                COALESCE(SUM(r.level), 0) AS total_levels
            FROM wc_players p
            LEFT JOIN wc_race_progress r ON r.steam_id = p.steam_id
            GROUP BY p.steam_id, p.name, p.global_xp
            ORDER BY total_levels DESC, p.global_xp DESC, p.name ASC
            LIMIT @limit;
            """,
            connection);

        command.Parameters.AddWithValue("@limit", Math.Clamp(limit, 1, 100));

        var result = new List<PlayerLeaderboardEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new PlayerLeaderboardEntry(
                reader.GetFieldValue<ulong>(0),
                reader.GetString(1),
                reader.GetInt64(2),
                reader.GetInt64(3)));
        }

        return result;
    }

    public async ValueTask SavePlayerAsync(
        PlayerPersistenceDto player,
        CancellationToken cancellationToken = default)
    {
        const int maxAttempts = 4;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                await UpsertPlayerAsync(connection, transaction, player, cancellationToken);
                await UpsertProgressAndPruneAsync(connection, transaction, player, cancellationToken);
                await UpsertStatsAsync(connection, transaction, player, cancellationToken);
                await UpsertAchievementsAndPruneAsync(connection, transaction, player, cancellationToken);
                await UpsertCosmeticsAsync(connection, transaction, player, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return;
            }
            catch (MySqlException exception) when (
                attempt < maxAttempts &&
                (exception.Number == 1213 || exception.Number == 1205))
            {
                await transaction.RollbackAsync(CancellationToken.None);

                // Deadlocks and lock wait timeouts are transient. Re-run the whole
                // transaction on a fresh connection after a short bounded delay.
                var delayMs = attempt switch
                {
                    1 => 25,
                    2 => 75,
                    _ => 150
                };

                await Task.Delay(delayMs, cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
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
            INSERT INTO wc_players (steam_id, name, global_xp, achievement_currency, active_race_id)
            VALUES (@steamId, @name, @globalXp, @achievementCurrency, @activeRaceId)
            ON DUPLICATE KEY UPDATE
                name = VALUES(name),
                global_xp = VALUES(global_xp),
                achievement_currency = VALUES(achievement_currency),
                active_race_id = VALUES(active_race_id);
            """,
            connection,
            transaction);

        command.Parameters.AddWithValue("@steamId", player.SteamId);
        command.Parameters.AddWithValue("@name", player.Name);
        command.Parameters.AddWithValue("@globalXp", player.GlobalXp);
        command.Parameters.AddWithValue("@achievementCurrency", player.AchievementCurrency);
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

    private static async Task UpsertProgressAndPruneAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        PlayerPersistenceDto player,
        CancellationToken cancellationToken)
    {
        var races = player.Races
            .OrderBy(x => x.RaceId, StringComparer.Ordinal)
            .ToArray();

        foreach (var race in races)
        {
            await using (var upsertRace = new MySqlCommand(
                """
                INSERT INTO wc_race_progress
                    (steam_id, race_id, level, xp, skill_points)
                VALUES
                    (@steamId, @raceId, @level, @xp, @skillPoints)
                ON DUPLICATE KEY UPDATE
                    level = VALUES(level),
                    xp = VALUES(xp),
                    skill_points = VALUES(skill_points);
                """,
                connection,
                transaction))
            {
                upsertRace.Parameters.AddWithValue("@steamId", player.SteamId);
                upsertRace.Parameters.AddWithValue("@raceId", race.RaceId);
                upsertRace.Parameters.AddWithValue("@level", race.Level);
                upsertRace.Parameters.AddWithValue("@xp", race.Xp);
                upsertRace.Parameters.AddWithValue("@skillPoints", race.SkillPoints);
                await upsertRace.ExecuteNonQueryAsync(cancellationToken);
            }

            var abilities = race.AbilityLevels
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .ToArray();

            foreach (var ability in abilities)
            {
                await using var upsertAbility = new MySqlCommand(
                    """
                    INSERT INTO wc_ability_progress
                        (steam_id, race_id, ability_id, level)
                    VALUES
                        (@steamId, @raceId, @abilityId, @level)
                    ON DUPLICATE KEY UPDATE
                        level = VALUES(level);
                    """,
                    connection,
                    transaction);

                upsertAbility.Parameters.AddWithValue("@steamId", player.SteamId);
                upsertAbility.Parameters.AddWithValue("@raceId", race.RaceId);
                upsertAbility.Parameters.AddWithValue("@abilityId", ability.Key);
                upsertAbility.Parameters.AddWithValue("@level", ability.Value);
                await upsertAbility.ExecuteNonQueryAsync(cancellationToken);
            }

            await PruneStaleAbilitiesAsync(
                connection,
                transaction,
                player.SteamId,
                race.RaceId,
                abilities.Select(x => x.Key).ToArray(),
                cancellationToken);
        }

        await PruneStaleRacesAsync(
            connection,
            transaction,
            player.SteamId,
            races.Select(x => x.RaceId).ToArray(),
            cancellationToken);
    }

    private static async Task PruneStaleAbilitiesAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        ulong steamId,
        string raceId,
        IReadOnlyList<string> abilityIds,
        CancellationToken cancellationToken)
    {
        if (abilityIds.Count == 0)
        {
            await using var deleteAll = new MySqlCommand(
                """
                DELETE FROM wc_ability_progress
                WHERE steam_id = @steamId AND race_id = @raceId;
                """,
                connection,
                transaction);

            deleteAll.Parameters.AddWithValue("@steamId", steamId);
            deleteAll.Parameters.AddWithValue("@raceId", raceId);
            await deleteAll.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        var names = abilityIds
            .Select((_, index) => $"@ability{index}")
            .ToArray();

        await using var delete = new MySqlCommand(
            $"""
            DELETE FROM wc_ability_progress
            WHERE steam_id = @steamId
              AND race_id = @raceId
              AND ability_id NOT IN ({string.Join(", ", names)});
            """,
            connection,
            transaction);

        delete.Parameters.AddWithValue("@steamId", steamId);
        delete.Parameters.AddWithValue("@raceId", raceId);

        for (var i = 0; i < abilityIds.Count; i++)
            delete.Parameters.AddWithValue(names[i], abilityIds[i]);

        await delete.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task PruneStaleRacesAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        ulong steamId,
        IReadOnlyList<string> raceIds,
        CancellationToken cancellationToken)
    {
        if (raceIds.Count == 0)
        {
            await using var deleteAll = new MySqlCommand(
                "DELETE FROM wc_race_progress WHERE steam_id = @steamId;",
                connection,
                transaction);

            deleteAll.Parameters.AddWithValue("@steamId", steamId);
            await deleteAll.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        var names = raceIds
            .Select((_, index) => $"@race{index}")
            .ToArray();

        await using var delete = new MySqlCommand(
            $"""
            DELETE FROM wc_race_progress
            WHERE steam_id = @steamId
              AND race_id NOT IN ({string.Join(", ", names)});
            """,
            connection,
            transaction);

        delete.Parameters.AddWithValue("@steamId", steamId);

        for (var i = 0; i < raceIds.Count; i++)
            delete.Parameters.AddWithValue(names[i], raceIds[i]);

        await delete.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertAchievementsAndPruneAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        PlayerPersistenceDto player,
        CancellationToken cancellationToken)
    {
        var achievements = player.Achievements.ToArray();

        foreach (var achievement in achievements)
        {
            await using var upsert = new MySqlCommand(
                """
                INSERT INTO wc_achievement_progress
                    (steam_id, achievement_id, progress, unlocked, unlocked_at)
                VALUES
                    (@steamId, @achievementId, @progress, @unlocked, @unlockedAt)
                ON DUPLICATE KEY UPDATE
                    progress = VALUES(progress),
                    unlocked = VALUES(unlocked),
                    unlocked_at = VALUES(unlocked_at);
                """,
                connection,
                transaction);

            upsert.Parameters.AddWithValue("@steamId", player.SteamId);
            upsert.Parameters.AddWithValue("@achievementId", achievement.AchievementId);
            upsert.Parameters.AddWithValue("@progress", achievement.Progress);
            upsert.Parameters.AddWithValue("@unlocked", achievement.Unlocked);
            upsert.Parameters.AddWithValue(
                "@unlockedAt",
                achievement.UnlockedAt?.UtcDateTime ?? (object)DBNull.Value);

            await upsert.ExecuteNonQueryAsync(cancellationToken);
        }

        if (achievements.Length == 0)
        {
            await using var deleteAll = new MySqlCommand(
                "DELETE FROM wc_achievement_progress WHERE steam_id = @steamId;",
                connection,
                transaction);
            deleteAll.Parameters.AddWithValue("@steamId", player.SteamId);
            await deleteAll.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        var names = achievements.Select((_, index) => $"@achievement{index}").ToArray();
        await using var prune = new MySqlCommand(
            $"""
            DELETE FROM wc_achievement_progress
            WHERE steam_id = @steamId
              AND achievement_id NOT IN ({string.Join(", ", names)});
            """,
            connection,
            transaction);

        prune.Parameters.AddWithValue("@steamId", player.SteamId);
        for (var index = 0; index < achievements.Length; index++)
            prune.Parameters.AddWithValue(names[index], achievements[index].AchievementId);

        await prune.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertCosmeticsAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        PlayerPersistenceDto player,
        CancellationToken cancellationToken)
    {
        var owned = player.OwnedCosmetics
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var cosmeticId in owned)
        {
            await using var upsert = new MySqlCommand(
                """
                INSERT INTO wc_cosmetic_inventory (steam_id, cosmetic_id)
                VALUES (@steamId, @cosmeticId)
                ON DUPLICATE KEY UPDATE cosmetic_id = VALUES(cosmetic_id);
                """,
                connection,
                transaction);

            upsert.Parameters.AddWithValue("@steamId", player.SteamId);
            upsert.Parameters.AddWithValue("@cosmeticId", cosmeticId);
            await upsert.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var clearEquipped = new MySqlCommand(
            "DELETE FROM wc_cosmetic_equipped WHERE steam_id = @steamId;",
            connection,
            transaction))
        {
            clearEquipped.Parameters.AddWithValue("@steamId", player.SteamId);
            await clearEquipped.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var (slot, cosmeticId) in player.EquippedCosmetics)
        {
            await using var upsertEquipped = new MySqlCommand(
                """
                INSERT INTO wc_cosmetic_equipped (steam_id, slot_id, cosmetic_id)
                VALUES (@steamId, @slotId, @cosmeticId)
                ON DUPLICATE KEY UPDATE cosmetic_id = VALUES(cosmetic_id);
                """,
                connection,
                transaction);

            upsertEquipped.Parameters.AddWithValue("@steamId", player.SteamId);
            upsertEquipped.Parameters.AddWithValue("@slotId", slot);
            upsertEquipped.Parameters.AddWithValue("@cosmeticId", cosmeticId);
            await upsertEquipped.ExecuteNonQueryAsync(cancellationToken);
        }

        if (owned.Length == 0)
        {
            await using var clearOwned = new MySqlCommand(
                "DELETE FROM wc_cosmetic_inventory WHERE steam_id = @steamId;",
                connection,
                transaction);
            clearOwned.Parameters.AddWithValue("@steamId", player.SteamId);
            await clearOwned.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        var names = owned.Select((_, index) => $"@cosmetic{index}").ToArray();
        await using var pruneOwned = new MySqlCommand(
            $"""
            DELETE FROM wc_cosmetic_inventory
            WHERE steam_id = @steamId
              AND cosmetic_id NOT IN ({string.Join(", ", names)});
            """,
            connection,
            transaction);

        pruneOwned.Parameters.AddWithValue("@steamId", player.SteamId);
        for (var index = 0; index < owned.Length; index++)
            pruneOwned.Parameters.AddWithValue(names[index], owned[index]);

        await pruneOwned.ExecuteNonQueryAsync(cancellationToken);
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
