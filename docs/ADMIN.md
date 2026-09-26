# Warcraft.Admin

All commands operate through `IWarcraftApi`. This module never reads or writes
the Warcraft database directly.

Required CounterStrikeSharp permission:

```text
@warcraft/admin
```

Server console/RCON can execute the commands without a player permission check.

Commands:

```text
!wc_xp <player> <amount>
!wc_level <player> <level>
!wc_race <player> <raceId>
!wc_reset <player>
!wc_givepoints <player> <amount>
!wc_reload_races
```

CounterStrikeSharp target expressions are supported because the commands use the
framework target parser. Commands that modify a player require exactly one
resolved online target and respect admin immunity.
