# Upgrading

## Standard upgrade

1. Take a database backup (`mysqldump warcraft > warcraft-backup.sql`).
2. Stop the server (or at least change map after step 4 — a full restart is
   the safest option).
3. Delete the old Warcraft plugin folders
   `addons/counterstrikesharp/plugins/Warcraft.*` so no stale DLL survives.
4. Copy the new `addons/counterstrikesharp/` layout from the release artifact,
   including `shared/Warcraft.Api/`.
5. Start the server and run `css_wc_status`: every module must report the new
   version and `Health: OK`.

Database schema changes are additive and applied automatically on start
(e.g. `wc_player_stats.rounds_played`, `wc_achievement_progress`). Progress is never dropped by an upgrade.

## Configs are not overwritten

Existing files in `game/csgo/configs/warcraft/` are kept. New keys use their
defaults until you add them (compare with `configs/warcraft/*.example.json`
in the repository).

Shipped race and shop files are copied only when the target is missing:

- `races/` — defaults are copied only into an **empty** directory. To get new
  or updated shipped races, copy the files you want from
  `plugins/Warcraft.Races/defaults/races/` into `configs/warcraft/races/`
  (the catalog hot-reloads; a broken file keeps the previous catalog).
- `shop.json` — copied only if missing; merge new items by hand.

Race `id`s are persistence keys: renaming an id orphans its progress;
deleting a race file hides the race but keeps the stored progress.

## Unlock requirements and existing players

Players keep access to every race they have selected before, even if the race
now has unlock requirements they do not meet. Admins can still assign any race
with `!wc_race <player> <raceId>`.

## Hot reload

CounterStrikeSharp hot reload is supported per plugin with these rules:

| Reloaded plugin | What to do |
|---|---|
| `Warcraft.Races` | nothing — the catalog reloads atomically |
| `Warcraft.Abilities`, `Menu`, `Admin`, `Vip`, `Shop`, `Achievements`, `Database` | nothing — they unregister and re-register through Core |
| `Warcraft.Core` | reload **every** `Warcraft.*` plugin afterwards (or change map/restart): other modules keep the previous Core API instance |

`Warcraft.Core` saves every connected player on unload (bounded by
`unloadSaveTimeoutSeconds`). Replacing `shared/Warcraft.Api/Warcraft.Api.dll`
always requires a full server restart.

## Rollback

Stop the server, restore the previous `addons/counterstrikesharp/` layout and,
if needed, the database backup from step 1.
