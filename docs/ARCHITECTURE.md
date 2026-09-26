# CS2Warcraft Architecture

## Hard rules

1. `Warcraft.Core` is the single owner of mutable player runtime state.
2. Feature modules communicate through `Warcraft.Api` only.
3. No feature module references another feature module.
4. No module except Core coordinates persistence.
5. `Warcraft.Database` implements storage contracts; it does not own gameplay state.
6. Races are data, not C# classes. A race is loaded from JSON and registered into Core.
7. Ability mechanics are reusable handlers registered by ID.
8. Ability handlers subscribe only to the events they need.
9. API consumers receive snapshots/DTOs, not mutable Core objects.
10. Hot reload replaces the race catalog atomically after validation.

## Runtime API

All feature plugins resolve the same CounterStrikeSharp capability:

```text
warcraft:core -> IWarcraftApi
```

`Warcraft.Api` contains only contracts and DTOs. It has no CounterStrikeSharp
dependency. The actual `PluginCapability<IWarcraftApi>` is declared by Core
and consumers use the same capability name.

## Runtime ownership

Core owns one mutable `PlayerRuntimeState` per loaded SteamID. Public callers
receive `PlayerStateSnapshot` values and can mutate state only through Core APIs.

## Race model

`Warcraft.Races` will load `configs/warcraft/races/*.json` and call
`api.Races.ReplaceCatalog(...)`. Core validates the full temporary catalog
before swapping it in, so a broken hot reload cannot partially replace races.

No race-specific C# branches are permitted in Core.

## Ability model

`Warcraft.Abilities` registers mechanics by ID. Race JSON references those IDs
and supplies race-specific config/conditions. Handlers subscribe to
`IWarcraftEventBus`, so there is no central race/ability switch.

## Active abilities, ultimates and cooldowns

Core owns player input (`css_ultimate`, `css_ability [slot]`) and every
eligibility rule: active race, learned level, `unlockLevel`, `conditions` and
cooldown. It then publishes `UltimatePressedEvent` / `AbilityPressedEvent`;
the handler that owns the ability id performs the mechanic and reports
success or failure. Core starts the cooldown (JSON `cooldown`, per level) only
after success. Cooldowns exist only in `PlayerRuntimeState.Cooldowns`.

## Conditions

Race ability `conditions` are compiled by Core together with the race catalog
(`RaceCatalogCompiler` → `CompiledRaceCatalog`). Handlers never evaluate them:
`api.Abilities.GetUsableAbility` returns `null` when any condition fails.

## Threading

- Engine callbacks, Core events, runtime state mutations and snapshots run on
  the game thread.
- Background work (MySQL, file watcher timers) hops back with
  `Server.NextFrame` / `Server.NextFrameAsync` before touching Core state.
- Registries (abilities, modifiers, menu, modules, race catalog swap) are
  thread-safe.

## Persistence model

`Warcraft.Database` registers one `IWarcraftStorageProvider`. Core coordinates
load/save timing and state conversion. Other modules never execute SQL.

## Planned dependency graph

```text
Warcraft.Api
   ^--- Warcraft.Core
   ^--- Warcraft.Database
   ^--- Warcraft.Races
   ^--- Warcraft.Abilities
   ^--- Warcraft.Menu
   ^--- Warcraft.Vip
   ^--- Warcraft.Admin
   ^--- Warcraft.Shop
```

No feature module references another feature module.
