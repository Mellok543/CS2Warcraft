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

Current saves replace one player's race/ability rows inside a transaction,
which keeps one authoritative snapshot per SteamID.
