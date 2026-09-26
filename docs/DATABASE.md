# Warcraft.Database

Runtime configuration:

```text
csgo/configs/warcraft/database.json
```

The plugin creates a safe disabled config on first start. Set `enabled` to
`true` after entering MySQL credentials.

## Tables

- `wc_players`
- `wc_race_progress`
- `wc_ability_progress`
- `wc_player_stats`

Only `Warcraft.Database` talks to MySQL. Other modules use `IWarcraftApi`.

Current saves replace one player's race/ability rows and upsert the stats row
inside one transaction, which keeps one authoritative snapshot per SteamID.

## Statistics

`wc_player_stats` stores lifetime totals: `kills`, `deaths`, `headshots`,
`rounds_played`, `rounds_won`, `play_seconds`. Core is the only writer of these
values:

```text
EventPlayerDeath / EventRoundEnd (CounterStrikeSharp)
      ↓ Core event bridge
PlayerKillEvent / PlayerDeathEvent / round participants
      ↓ StatsService (Core, game thread)
PlayerStatsRuntime → PlayerStateChangedEvent("stats:*")
      ↓ PersistenceSaveScheduler (debounced)
PlayerPersistenceDto.Stats → Warcraft.Database → MySQL
```

Teamkills are not counted as kills. `play_seconds` is the stored total plus
the time since the player connected, so every save writes an absolute value.
`Warcraft.Database` never listens to game events.

Databases created by earlier versions get the `rounds_played` column added
automatically on startup.

## Threading

- Runtime state is captured on the game thread (`Server.NextFrameAsync`) and
  storage I/O runs on the thread pool.
- Loaded state is swapped in on the game thread.
- Saves of one player are serialized, and a disconnect save supersedes a
  pending debounced save, so an older snapshot never overwrites a newer one.
