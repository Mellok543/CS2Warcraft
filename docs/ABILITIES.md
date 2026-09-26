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
| `speed`           | passive  | `GameTickEvent`          | `multiplier` (> 1.0)                                 |
| `low_gravity`     | passive  | `GameTickEvent`          | `gravity` (0.05 .. 1.0)                              |
| `regeneration`    | passive  | `GameTickEvent`          | `amount`, `interval` (seconds, default 1)            |
| `evasion`         | passive  | `DamagePreEvent`         | `chance`                                             |
| `reflect_damage`  | passive  | `DamagePostEvent`        | `percent`, `maxDamage` (optional cap per hit)        |
| `dash`            | active   | `AbilityPressedEvent`    | `force`, `upForce`, `cooldown`                     |
| `bash`            | passive  | `DamagePostEvent` + tick | `chance`, `duration` (s), `slow` (0 = freeze)      |
| `invisibility`    | passive  | `GameTickEvent`          | `alpha` (0 invisible .. 255 normal)                 |
| `damage_reduction`| passive  | `DamagePreEvent`         | `percent` (max 0.9)                                 |

`speed` and `low_gravity` keep the pawn attribute at the configured value while
the ability is usable and restore the default when it stops being usable (for
example when a `weaponTypes` condition fails after a weapon switch). The engine
damage slowdown is respected: speed is re-applied once it has recovered.
`evasion` only negates damage dealt by players (not fall/world damage); a fully
negated hit is blocked by Core. `reflect_damage` applies to human attackers
and, like chain lightning, deals damage through `api.Combat` (kills are credited).

`GameTickEvent` is published by Core every `gameTickIntervalMilliseconds`
(core.json, default 100 ms).

`dash` changes velocity (not position), so it cannot put a player inside a wall.
`invisibility` affects the player model; held weapons stay visible.

The same mechanic is configured differently per race — for example `speed`:

```json
// human.json                         // undead.json
"multiplier": [1.05, 1.10, 1.15]      "multiplier": [1.10, 1.20, 1.30]
```

Every numeric value is either a scalar or an array indexed by ability level:

```json
"config": {
  "chance": [0.08, 0.14, 0.20],
  "damage": 35
}
```

## Descriptions

Every handler registers a default Russian description template; a race can
override it per ability with `"description"`. Core fills placeholders from the
race config at the relevant level:

| Placeholder     | Example config         | Output (level 2) |
|-----------------|------------------------|------------------|
| `{damage}`      | `"damage": 35`         | `35`             |
| `{chance%}`     | `"chance": [0.1, 0.2]` | `20%`            |
| `{interval\|1}` | key missing            | `1`              |

```json
{ "id": "reflect_damage",
  "description": "Возвращает атакующему {percent%} урона, но не больше {maxDamage} за удар.",
  "config": { "percent": [0.10, 0.18, 0.25], "maxDamage": 20 } }
```

A race `description` that references a key missing from its config (and without
a `|default`) rejects the reload. Conditions are described automatically
(`оружие: винтовка; HP ≥ 50%`). The menu shows an ability card (current and
next level, requirements, conditions, bind hint) and a race preview with every
ability before the race is selected.

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
pipeline, so it does not trigger critical strike or vampirism).

Ability damage goes through `api.Combat.DealAbilityDamage`. A lethal hit is
executed as a forced suicide, and Core rewrites the attacker in the
`player_death` pre-hook: the kill feed shows the ability owner, who receives
kill XP and a kill in the statistics. The CS scoreboard is not adjusted.

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
