# Configuration

All runtime configs live in `game/csgo/configs/warcraft/`. Missing files are
created with defaults on first start. JSON comments and trailing commas are
allowed.

## core.json (Warcraft.Core)

| Key | Default | Meaning |
|---|---|---|
| `killXp` | 25 | XP for killing an enemy player (teamkills give nothing) |
| `headshotBonusXp` | 10 | extra XP for a headshot kill |
| `assistXp` | 10 | XP for an assist |
| `roundWinXp` | 15 | XP for every player of the winning team |
| `bombPlantXp` | 20 | XP for planting the bomb |
| `bombDefuseXp` | 20 | XP for defusing the bomb |
| `autosaveDelayMilliseconds` | 1000 | debounce before a changed player is saved |
| `gameTickIntervalMilliseconds` | 100 | period of `GameTickEvent` (auras, regeneration, speed) |
| `unloadSaveTimeoutSeconds` | 5 | hard budget for the final save when Core unloads |

XP needed from level N to N+1 is `100 × N²`; every level grants one skill
point (plus the VIP `bonusSkillPointsPerLevel`). VIP `xpMultiplier` is applied
to every XP source. Core reads `core.json` on load; restart or reload Core
(see [UPGRADE.md](UPGRADE.md#hot-reload)) after editing.

## database.json (Warcraft.Database)

| Key | Default | Meaning |
|---|---|---|
| `enabled` | false | must be `true` to connect |
| `host`, `port` | 127.0.0.1, 3306 | MySQL server |
| `database`, `user`, `password` | warcraft, warcraft, change-me | credentials |
| `minimumPoolSize`, `maximumPoolSize` | 1, 20 | connection pool |
| `connectionTimeoutSeconds` | 10 | connect timeout |

Schema is created and migrated automatically. See [DATABASE.md](DATABASE.md).

## vip.json (Warcraft.Vip)

| Key | Default | Meaning |
|---|---|---|
| `permission` | `@warcraft/vip` | admin flag that marks a VIP |
| `xpMultiplier` | 1.5 | multiplier for all XP |
| `bonusSkillPointsPerLevel` | 0 | extra skill points per level |
| `canAccessVipRaces` | true | may select `"vipOnly": true` races |
| `extraRaceSlots` | 0 | reserved |
| `shopDiscount` | 0.2 | shop price reduction (0.2 = 20%) |

See [VIP.md](VIP.md).

## shop.json (Warcraft.Shop)

Items with `price`, `maxPerRound`, `requiresAlive` and an `effect`
(`heal`, `armor`, `give_item`, `xp`). Reload with `!wc_reload_shop`.
See [SHOP.md](SHOP.md).

## visuals.json (Warcraft.Abilities)

| Key | Default | Meaning |
|---|---|---|
| `models` | true | spawn totem, turret, roots, shield bubble and aura ring models from the Warcraft UI addon; set `false` if the server does not mount the addon |
| `particles` | true | play ability particle effects from the same addon (bursts, auras, lightning, shockwaves, status effects); `false` falls back to beams for rays and totem areas |

## races/*.json (Warcraft.Races)

One file per race. Saving a file hot-reloads the whole catalog; any error keeps
the previous catalog. Schema, requirements and the unlock tree:
[RACES.md](RACES.md). Mechanics, config keys, conditions and description
placeholders: [ABILITIES.md](ABILITIES.md).

## admins.json (CounterStrikeSharp)

| Flag | Grants |
|---|---|
| `@warcraft/admin` | Warcraft admin commands ([ADMIN_COMMANDS.md](ADMIN_COMMANDS.md)) |
| `@warcraft/vip` | VIP benefits (configurable in `vip.json`) |


## Progression balance

Race unlocks are progression-based, never time-gated. The default catalog combines total race levels, lifetime Global XP, and parent-race levels.

The target pacing for a normal non-boosted player is roughly 70–100 hours to reach the final race tier. This is an economy target, not a hard timer: highly effective players or XP boosts may progress faster.

| Tier | Total race levels | Global XP | Parent race |
| --- | ---: | ---: | ---: |
| II | 10 | 15,000 | level 5 |
| III | 30 | 50,000 | level 7 |
| IV | 60 | 110,000 | level 8 |
| V | 100 | 220,000 | level 10 |

Race-level XP uses `150 * level²` for each next level. A race therefore needs 42,750 XP to progress from level 1 to level 10.

`jump_boost` supports `maxSpeed` and `cooldown`; both prevent repeated long jumps from compounding into unlimited bhop acceleration.

