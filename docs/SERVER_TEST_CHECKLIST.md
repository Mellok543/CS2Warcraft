# CS2Warcraft server smoke-test checklist

Run this checklist on a real Counter-Strike 2 server before tagging a release.

## Installation and loading

- [ ] Start from a clean CounterStrikeSharp server.
- [ ] Copy the staged `artifacts/addons/counterstrikesharp/` layout.
- [ ] Confirm exactly one shared `Warcraft.Api.dll` is loaded.
- [ ] Confirm all Warcraft modules load without exceptions.
- [ ] Confirm `Warcraft.Database` connects and schema migration succeeds.
- [ ] Confirm plugin load order does not matter after `OnAllPluginsLoaded`.
- [ ] Confirm a hot reload does not duplicate menu entries, modifiers or handlers.

## Player lifecycle and persistence

- [ ] First-time player joins and receives an empty profile.
- [ ] Existing player joins and persisted race/progress/statistics are restored.
- [ ] Player earns XP, disconnects immediately, reconnects and keeps it.
- [ ] Player upgrades an ability, reconnects and keeps the level and skill-point spend.
- [ ] Player changes race, reconnects and keeps the active race.
- [ ] Restart the server and verify the same persisted values again.
- [ ] Change map and verify loaded players keep valid runtime state.
- [ ] Verify two quick saves for one SteamID never regress to an older snapshot.

## Race catalog

- [ ] `!wc_reload_races` successfully reloads a valid edit.
- [ ] Break one JSON file and verify the previous catalog remains active.
- [ ] Restore the file and verify reload succeeds.
- [ ] Verify starter races can be selected.
- [ ] Verify locked races show their requirements and cannot be bypassed from normal menu flow.
- [ ] Verify VIP-only races are rejected by Core for a non-VIP player.
- [ ] Verify admin forced race selection behaves as intended.

## Progression

- [ ] Normal kill awards configured kill XP.
- [ ] Headshot awards kill XP plus headshot bonus.
- [ ] Teamkill does not award normal kill XP.
- [ ] VIP XP multiplier is applied exactly once.
- [ ] Level-up grants the configured skill points.
- [ ] Ability upgrade enforces unlock level, max level and available skill points.

## Abilities and combat

- [ ] `critical_strike` modifies outgoing damage only when it procs.
- [ ] `evasion` can fully cancel an eligible hit.
- [ ] `bonus_health` applies correctly after spawn.
- [ ] `vampirism` heals without exceeding max health.
- [ ] Active ability command triggers the configured active ability.
- [ ] Ultimate command respects cooldown and unlock requirements.
- [ ] `chain_lightning` damages valid enemy targets and respects jump/range config.
- [ ] A lethal chain-lightning hit credits the caster in kill feed, XP and stats.
- [ ] A lethal reflected-damage hit credits the reflector in kill feed, XP and stats.
- [ ] Ability kill credit does not survive past its TTL.
- [ ] Ability kill credit does not leak to a player reusing the same slot.
- [ ] Attacker disconnect during a pending ability kill does not crash.
- [ ] Victim disconnect during a pending ability kill does not crash.
- [ ] Round start clears stale pending ability damage attribution.
- [ ] No reflect/ability recursion creates an infinite damage loop.

## Statistics and objectives

- [ ] Kills/deaths/headshots update once per event.
- [ ] Assists update once per valid assist.
- [ ] Round played/won counters update correctly for both teams.
- [ ] Bomb plant/defuse rewards and statistics are correct.
- [ ] Playtime increases across a session and survives reconnect/restart.

## UI and optional modules

- [ ] `!wc` opens the root menu.
- [ ] Race selection, current race, abilities and statistics menus work.
- [ ] Shop registers one root menu entry and purchases work.
- [ ] VIP registers one root menu entry and displays the expected modifiers.
- [ ] Admin commands enforce permission and immunity.

## Stability

- [ ] Run at least one full match with 4+ human players.
- [ ] Change map at least twice.
- [ ] Reload race JSON repeatedly while players are connected.
- [ ] Exercise abilities while players connect/disconnect.
- [ ] Review server logs for exceptions, duplicate registrations and persistence errors.

## Diagnostics

- [ ] `css_wc_status` from the server console lists all nine modules with the release version.
- [ ] A client without the Warcraft UI Workshop addon is documented as unsupported; with the addon `!wc` renders and W/S/A/D/E/R navigate.
- [ ] Reconnect while the database is slow (or right after connecting, before the load finishes): stored XP, races and achievements are unchanged afterwards.
- [ ] Totems show their model (healing/flame/frost/war/shield) facing the caster's view, standing on the ground, removed when they expire or are recast.
- [ ] Aura owners have a tinted ring at their feet that follows them and disappears on death/race change.
- [ ] Divine shield shows a golden bubble for its duration; entangle shows the roots ring at the target.
- [ ] With `visuals.json` → `"models": false`, no models spawn and totems use beam pillars.
- [ ] Particles (addon mounted): totems show a shimmering circle exactly at their radius plus ambient effects (green motes, fire in the flame bowl, falling snow, red/gold motes); the engineer turret turns towards its target and fires a tracer with sparks.
- [ ] Auras show rising coloured motes around the owner (immolation: a ring of flames).
- [ ] Chain lightning draws jagged blue bolts between targets; smite drops a golden bolt and pillar; life drain, pull and swap draw red/violet rays; war stomp, repulse, battle cry and heal burst show a shockwave along the ground.
- [ ] Status effects follow the player and end with the effect: poison (green), stun from bash/war stomp (blue), frost totem slow, rage flames, divine shield sparkles, sprint, battle cry buff.
- [ ] Level up shows a golden pillar on the player; resurrect and reincarnation show a pillar at the respawn point.
- [ ] With `visuals.json` → `"particles": false`, rays fall back to beams and totems draw beam rings; nothing else is spawned.
- [ ] Using any ability does not advance `mechanic.*` achievements; chain lightning hitting 3 targets unlocks "Цепная реакция".
- [ ] `Persistence` shows `warcraft.mysql`; with `database.json` disabled it shows the missing-provider warning.
- [ ] `Health: OK` with the shipped races. Changing an ability id in a race JSON to an unknown id still reloads, and `css_wc_status` then lists it as `has no handler`; a JSON syntax error shows `Last race reload: REJECTED`.
- [ ] The race health line is logged by Warcraft.Races after startup and after each reload.
- [ ] Ability damage (chain lightning, flame totem) is reduced by `divine_shield` / `shield_totem` / `damage_reduction` and never triggers `reflect_damage`.
