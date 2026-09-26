# Warcraft.Races

Runtime race directory:

```text
csgo/configs/warcraft/races/*.json
```

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

Shipped examples: `orc`, `human`, `undead`, `night_elf`. They reuse the same
handlers with different values and conditions — no race has C# code.

Other modules can request a reload without referencing `Warcraft.Races`:

```csharp
api.Events.Publish(new RaceReloadRequestedEvent("warcraft.admin"));
```
