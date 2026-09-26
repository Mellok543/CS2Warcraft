# Warcraft.Shop

Open with `!shop` or the "Магазин" entry in `!wc`. Items are paid with
in-game money. The price includes the Core-combined `ShopDiscount` modifier
(e.g. VIP), so the shop never checks VIP itself.

Runtime config `csgo/configs/warcraft/shop.json` is copied from the plugin's
`defaults/shop.json` on first start. Reload with `!wc_reload_shop`
(`@warcraft/admin`). Invalid items are logged and skipped.

```json
{
  "items": [
    {
      "id": "health_potion",
      "name": "Зелье здоровья",
      "description": "Восстанавливает 40 HP.",
      "price": 800,
      "maxPerRound": 2,          // 0 = unlimited, reset on round start
      "requiresAlive": true,     // default true
      "effect": { "type": "heal", "amount": 40 }
    }
  ]
}
```

| Effect type | Parameters                  | Result                                   |
|-------------|-----------------------------|------------------------------------------|
| `heal`      | `amount`                    | heals up to max health                   |
| `armor`     | `amount`, `helmet` (bool)   | armor up to 100, optional helmet         |
| `give_item` | `item` (`weapon_*`/`item_*`)| gives the item                           |
| `xp`        | `amount`                    | `api.Progress.AddXp` (modifiers apply)   |

A new effect is one `IShopEffect` class registered in
`ShopEffectRegistry.CreateDefault`. Money is only charged after the effect was
applied successfully.
