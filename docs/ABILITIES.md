# Warcraft.Abilities

Ability mechanics are implemented once and registered by ID. A race only
references those IDs in JSON and supplies its own `config` and `conditions`.
No handler checks the active race ID: Core resolves the player's active race
and returns the effective `PlayerAbilitySnapshot`.

## Handlers

| ID                | Kind     | Trigger                  | Config keys                                          |
|-------------------|----------|--------------------------|------------------------------------------------------|
| `critical_strike` | passive  | `DamagePreEvent`         | `chance`, `damageMultiplier`                         |
| `bonus_health`    | passive  | `PlayerSpawnEvent`       | `health`                                             |
| `vampirism`       | passive  | `DamagePostEvent`        | `percent`                                            |
| `chain_lightning` | ultimate | `UltimatePressedEvent`   | `damage`, `range`, `jumps`, `damageFalloff`, `cooldown` |

Every numeric value is either a scalar or an array indexed by ability level:

```json
"config": {
  "chance": [0.08, 0.14, 0.20],
  "damage": 35
}
```

## Usability

Handlers call `api.Abilities.GetUsableAbility(steamId, id)`. Core returns a
snapshot only when **all** of these hold, otherwise `null`:

1. the player has an active race that contains the ability;
2. the ability level is at least 1;
3. the race level is at least `unlockLevel`;
4. every entry of `conditions` is satisfied.

## Conditions

`conditions` are compiled by Core when the race catalog is (re)loaded. Unknown
keys or malformed values reject the whole reload, so the previous catalog keeps
working. Conditions are evaluated against the ability owner.

| Key                | Value                          | Meaning                                   |
|--------------------|--------------------------------|-------------------------------------------|
| `weaponTypes`      | array of categories            | owner's active weapon category is listed  |
| `minHealthPercent` | number `0.0..1.0`              | health / max health >= value              |
| `maxHealthPercent` | number `0.0..1.0`              | health / max health <= value              |
| `requiresGrounded` | `true` / `false`               | owner is (not) standing on the ground     |

Weapon categories: `knife`, `pistol`, `smg`, `rifle`, `shotgun`, `sniper`,
`machinegun`, `taser`, `grenade`, `c4`, `equipment`.

```json
"conditions": {
  "weaponTypes": ["rifle", "smg"],
  "minHealthPercent": 0.5
}
```

A new condition is one `IAbilityConditionFactory` in
`Warcraft.Core/Conditions/Builtin` plus one line in
`AbilityConditionRegistry.CreateDefault`.

## Active abilities and ultimates

Players activate abilities with console commands (bindable) or chat:

```text
bind x css_ultimate        // !ultimate
bind c "css_ability 1"     // !ability 1
```

`css_ability <slot>` addresses the race's abilities whose handler registered
`AbilityKind.Active`, numbered in JSON order starting at 1. The ultimate is the
race's `ultimate` block.

Activation pipeline (all checks in Core, mechanics in the handler):

```text
css_ultimate
  → Core: player alive, active race, ultimate defined, handler registered
  → Core: learned level, unlockLevel, conditions (GetUsableAbility rules)
  → Core: cooldown remaining?  → "перезаряжается: N с"
  → Core publishes UltimatePressedEvent { SteamId, Ability }
  → handler: if (!e.IsFor(Id)) return; perform mechanic; e.Succeed() / e.Fail(reason)
  → Core: on success start cooldown from config "cooldown" (seconds, per level)
  → Core publishes AbilityActivatedEvent
```

Cooldowns live only in Core (`PlayerRuntimeState.Cooldowns`). Handlers never
keep their own timers; a failed activation (e.g. no targets) does not start the
cooldown.

## chain_lightning

Strikes the nearest enemy within `range`, then jumps to the nearest enemy
around the previous target that has not been hit yet, up to `jumps` extra
times. Each jump multiplies damage by `damageFalloff` (default `1.0`).
Damage is applied directly to health (it does not re-enter the weapon damage
pipeline, so it does not trigger critical strike or vampirism). A lethal bolt
kills the target without kill credit.

```json
"ultimate": {
  "id": "chain_lightning",
  "maxLevel": 1,
  "unlockLevel": 6,
  "config": { "damage": 35, "range": 500, "jumps": 4, "cooldown": 25 },
  "conditions": {}
}
```

## Writing a handler

Derive from `AbilityHandler`, choose `Kind`, subscribe to `Warcraft.Api`
events in `Subscribe`, read values with `AbilityConfigReader`, and register the
instance in `WarcraftAbilitiesPlugin`. Any race can then use it from JSON.
