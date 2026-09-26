# Warcraft.Vip

VIP players are identified by a CounterStrikeSharp admin flag (default
`@warcraft/vip`) in `addons/counterstrikesharp/configs/admins.json`:

```json
{
  "PlayerName": { "identity": "76561198XXXXXXXXX", "flags": ["@warcraft/vip"] }
}
```

Runtime config `csgo/configs/warcraft/vip.json` (created on first start):

```json
{
  "permission": "@warcraft/vip",
  "xpMultiplier": 1.5,
  "bonusSkillPointsPerLevel": 0,
  "canAccessVipRaces": true,
  "extraRaceSlots": 0,
  "shopDiscount": 0.2
}
```

The module only registers an `IPlayerModifierProvider`. It never changes XP,
races or the shop directly:

| Benefit                    | Applied by                                   |
|----------------------------|----------------------------------------------|
| `xpMultiplier`             | Core `Progress.AddXp`                        |
| `bonusSkillPointsPerLevel` | Core level-up                                |
| `canAccessVipRaces`        | Core `Races.SelectRace` (`"vipOnly": true`)  |
| `shopDiscount`             | Warcraft.Shop via `api.Modifiers.GetCombined`|
| `extraRaceSlots`           | reserved; races can be switched freely today |

Players see their status with `!vip` or the "VIP" entry in `!wc`.
