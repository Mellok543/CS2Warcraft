# Warcraft.Admin

The module uses its own compact letter flags from `configs/warcraft/admin.json`.

## Flags

| Flag | Access |
|---|---|
| `b` | bans / unbans |
| `k` | kick, warnings, clear warnings |
| `m` | mute / unmute |
| `g` | gag / ungag |
| `f` | fun commands |
| `i` | change map, restart round |
| `z` | root access: all flags + Warcraft progression/service commands |

Server console/RCON bypasses flag checks.

Example:

```json
{
  "admins": {
    "76561198000000000": {
      "name": "Owner",
      "flags": "z",
      "immunity": 100
    },
    "76561198000000001": {
      "name": "Moderator",
      "flags": "bkmg",
      "immunity": 20
    }
  },
  "warnKickThreshold": 3,
  "banDurationsMinutes": [30, 120, 1440, 10080, 0],
  "muteDurationsMinutes": [10, 30, 120, 1440, 0],
  "gagDurationsMinutes": [10, 30, 120, 1440, 0]
}
```

`0` duration means permanent.

Punishments are saved automatically to
`configs/warcraft/admin_punishments.json`.

## Menu

- `!admin`
- `!a`

The menu contains only actions allowed by the caller's flags.

## Moderation commands

| Command | Flag |
|---|---|
| `!ban <player> <minutes|0> [reason]` | b |
| `!unban <steamid64>` | b |
| `!kick <player> [reason]` | k |
| `!warn <player> [reason]` | k |
| `!clearwarns <player>` | k |
| `!mute <player> <minutes|0> [reason]` | m |
| `!unmute <player>` | m |
| `!gag <player> <minutes|0> [reason]` | g |
| `!ungag <player>` | g |

Bans, mutes, gags and warnings persist across restarts. Expired timed
punishments are pruned automatically.

## Fun commands

| Command | Effect |
|---|---|
| `!slay <player>` | kill player |
| `!slap <player> [damage]` | damage + vertical impulse |
| `!freeze <player>` | freeze movement |
| `!unfreeze <player>` | restore walking |
| `!hp <player> <1..1000>` | set HP |
| `!money <player> <0..16000>` | set money |
| `!bring <player>` | teleport target to admin |

All require `f`.

## Server commands

| Command | Effect |
|---|---|
| `!map <map>` | change map |
| `!rr` | restart round after 1 second |

Both require `i`.

The map menu reads the Warcraft RTV map list when present.

## Warcraft root commands

These require `z`:

- `!wc_xp <player> <amount>`
- `!wc_level <player> <level>`
- `!wc_race <player> <raceId>`
- `!wc_reset <player>`
- `!wc_givepoints <player> <amount>`
- `!wc_reload_races`
- `!wc_status`
- `!wc_reload_admin`

Targeting respects both Warcraft.Admin immunity and CounterStrikeSharp
admin immunity.
