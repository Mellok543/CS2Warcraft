# Admin commands

All commands go through `IWarcraftApi`; no admin module reads or writes the
database directly.

- Permission: `@warcraft/admin` (in `addons/counterstrikesharp/configs/admins.json`).
- The server console / RCON runs every command without a permission check —
  use the `css_` form there.
- In chat use `!` instead of `css_` (`!wc_xp` = `css_wc_xp`).
- `<player>` accepts CounterStrikeSharp target expressions (name part, `#userid`,
  `@me`, …). Commands that change a player need exactly one online human target
  and respect admin immunity.

## Warcraft.Admin

| Command | Effect |
|---|---|
| `css_wc_status` | diagnostics: Core version, modules and versions, registered abilities, loaded races, persistence provider, loaded players, last race reload, race catalog health |
| `css_wc_xp <player> <amount>` | add (or remove, negative) XP to the active race; the VIP multiplier applies to positive amounts |
| `css_wc_level <player> <level>` | set the level of the active race |
| `css_wc_givepoints <player> <amount>` | add skill points to the active race |
| `css_wc_race <player> <raceId>` | switch the active race, **bypassing** VIP and unlock requirements |
| `css_wc_reset <player>` | reset all Warcraft progress of the player (races, levels, XP, cooldowns; statistics are kept) |
| `css_wc_reload_races` | reload `configs/warcraft/races/*.json`; on any error the previous catalog stays |

## Warcraft.Shop

| Command | Effect |
|---|---|
| `css_wc_reload_shop` | reload `configs/warcraft/shop.json` (invalid items are logged and skipped) |

## Reading `css_wc_status`

```text
[Warcraft] Core 0.6.0 | players loaded: 12
[Warcraft] Persistence: warcraft.mysql
[Warcraft] Modules: warcraft.abilities 0.6.0, warcraft.admin 0.6.0, ...
[Warcraft] Abilities registered: 59 | races loaded: 30
[Warcraft] Races: starter 3 [human, orc, undead], locked 26, VIP 1 [shadow]
[Warcraft] Last race reload: OK from plugin-load at 12:00:01 UTC, errors: 0
[Warcraft] Health: OK
```

Problems are listed with `!`:

- `Persistence: НЕТ ПРОВАЙДЕРА` — Warcraft.Database is missing, disabled
  (`database.json` → `"enabled": true`) or could not connect (see its log).
- `ability 'x' has no handler` — a race references a mechanic that is not
  registered (typo in JSON or Warcraft.Abilities not loaded).
- `ultimate 'x' is a passive mechanic` — the ultimate can never be activated.
- `Last race reload: REJECTED` — the newest files are broken; the previous
  catalog is still active. The first errors are printed below the line.

The same race health report is logged by Warcraft.Races after every
successful reload.
