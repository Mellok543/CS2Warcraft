# Warcraft.Races

Runtime race directory:

```text
csgo/configs/warcraft/races/*.json
```

Default races are copied only when the directory is empty; copy new shipped
JSON files manually on existing servers.

A new race requires only a new JSON file as long as every referenced ability ID
has an implementation in `Warcraft.Abilities`.

The loader watches JSON files and performs a debounced hot reload. It parses all
files first and only then asks Core to replace the active race catalog.

If parsing or Core validation fails, the previous catalog remains active.
Core validation covers ids, levels, the Core-owned `cooldown` key and every
`conditions` entry (see [ABILITIES.md](ABILITIES.md#conditions)).

## Schema

```json
{
  "id": "undead",                 // unique, used in persistence
  "name": "Нежить",
  "description": "...",
  "maxLevel": 10,
  "vipOnly": false,               // enforced by Core (CanAccessVipRaces)
  "requirements": {               // optional; omit for races open from the start
    "totalLevel": 14,             // sum of the player's levels over all races
    "globalXp": 0,                // lifetime XP
    "races": { "undead": 6 }      // minimum level of specific races
  },
  "abilities": [
    {
      "id": "speed",              // handler id from Warcraft.Abilities
      "name": "Нечестивая аура",  // optional race-specific display name
      "description": "Скорость x{multiplier}.", // optional, overrides the handler text
      "maxLevel": 3,
      "unlockLevel": 1,           // required race level to learn / use
      "config": { "multiplier": [1.10, 1.20, 1.30] },
      "conditions": {}
    }
  ],
  "ultimate": {                   // optional, activated with css_ultimate
    "id": "chain_lightning",
    "maxLevel": 2,
    "unlockLevel": 6,
    "config": { "damage": [25, 35], "range": 400, "jumps": [2, 3], "cooldown": [30, 22] },
    "conditions": {}
  }
}
```

Unlock requirements are validated with the catalog (unknown race ids, self
reference, level bounds) and enforced by Core in `SelectRace`; once a player
has selected a race it stays available. Admin `!wc_race` bypasses them. The
race menu shows locked races with per-requirement progress.

Shipped unlock tree (30 races):

| Tier | Condition                    | Races                                                                 |
|------|------------------------------|-----------------------------------------------------------------------|
| 1    | open                         | orc, human, undead                                                    |
| 2    | total level 6 (+ race)       | night_elf (undead 3), paladin (human 4), troll (orc 4), goblin, trickster |
| 3    | total level 14 (+ race)      | druid (night_elf 4), tauren (orc 6), shaman (troll 4), assassin (undead 6), butcher, warlord (paladin 4) |
| 4    | total level 24 + race        | frost_mage (undead 6), fire_lord (orc 6), priest (paladin 6), crusader (human 8), vampire_lord (undead 8), chieftain (warlord 5), wind_walker (night_elf 5), berserker (troll 6) |
| 5    | total level 36 + race        | witch_doctor (shaman 6), dwarf (goblin 6), ranger (night_elf 8), necromancer (vampire_lord 5), storm_spirit (frost_mage 5), guardian (crusader 5), spirit_walker (druid 6) |
| VIP  | `@warcraft/vip`              | shadow                                                                |

Other modules can request a reload without referencing `Warcraft.Races`:

```csharp
api.Events.Publish(new RaceReloadRequestedEvent("warcraft.admin"));
```
