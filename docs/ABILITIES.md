# Warcraft.Abilities

Ability mechanics are implemented once and registered by ID.

Current handlers:

- `critical_strike` — subscribes to `DamagePreEvent`
- `bonus_health` — subscribes to `PlayerSpawnEvent`
- `vampirism` — subscribes to `DamagePostEvent`

A race only references those IDs and supplies its own `config` values.

Example:

```json
{
  "id": "vampirism",
  "maxLevel": 3,
  "unlockLevel": 3,
  "config": {
    "percent": [0.08, 0.12, 0.18]
  }
}
```

No ability handler checks the active race ID. Core resolves the player's active
race and returns the effective `PlayerAbilitySnapshot`.

Active abilities and ultimates will use the same registration/event model and
Core cooldown service.
