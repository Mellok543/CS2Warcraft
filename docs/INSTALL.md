# Installation

## Requirements

- CS2 dedicated server with Metamod:Source and CounterStrikeSharp **1.0.375+**
  (the .NET runtime shipped with CounterStrikeSharp).
- MySQL 8 or MariaDB 10.5+ (optional but required to keep progress between
  sessions).

## 1. Get the build

Either download the `cs2warcraft-<version>` artifact from the latest green
GitHub Actions run, or build it yourself:

```bash
dotnet build CS2Warcraft.slnx -c Release
```

The Release build stages a drop-in layout in `artifacts/addons/counterstrikesharp/`.

## 2. Copy

Copy the **contents** of `artifacts/addons/counterstrikesharp/` into
`game/csgo/addons/counterstrikesharp/`:

```text
addons/counterstrikesharp/
├─ shared/
│  └─ Warcraft.Api/Warcraft.Api.dll      ← contracts, loaded ONCE for all plugins
└─ plugins/
   ├─ Warcraft.Core/                      ← required
   ├─ Warcraft.Database/                  ← MySQL provider (+ MySqlConnector.dll)
   ├─ Warcraft.Races/                     ← JSON races (+ defaults/races/*.json)
   ├─ Warcraft.Abilities/                 ← ability mechanics
   ├─ Warcraft.Menu/                      ← !wc menu
   ├─ Warcraft.Admin/                     ← admin commands
   ├─ Warcraft.Vip/                       ← optional VIP modifiers
   └─ Warcraft.Shop/                      ← optional item shop (+ defaults/shop.json)
```

Rules:

- `Warcraft.Api.dll` must exist **only** in `shared/Warcraft.Api/`. A copy
  inside a plugin folder gives that plugin its own `IWarcraftApi` type and the
  `warcraft:core` capability cannot be resolved.
- Never copy `bin/` folders from a build directly.
- `Warcraft.Vip` and `Warcraft.Shop` can be left out; every other module is
  needed for normal gameplay. Without `Warcraft.Database` progress lives only
  in memory.

## 3. First start

Start the server once. Warcraft creates its configs under
`game/csgo/configs/warcraft/`:

```text
core.json        XP rewards, timings
database.json    MySQL connection (created DISABLED)
vip.json         VIP benefits
shop.json        shop items (copied from plugins/Warcraft.Shop/defaults)
races/*.json     30 races (copied from plugins/Warcraft.Races/defaults/races)
```

See [CONFIGURATION.md](CONFIGURATION.md) for every key.

## 4. Database

1. Create a database and user:

   ```sql
   CREATE DATABASE warcraft CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
   CREATE USER 'warcraft'@'%' IDENTIFIED BY 'strong-password';
   GRANT ALL PRIVILEGES ON warcraft.* TO 'warcraft'@'%';
   ```

2. Fill `configs/warcraft/database.json` and set `"enabled": true`.
3. Restart the server. Tables `wc_players`, `wc_race_progress`,
   `wc_ability_progress`, `wc_player_stats` are created automatically.

## 5. Admins and VIP

In `addons/counterstrikesharp/configs/admins.json`:

```json
{
  "Admin": { "identity": "76561198XXXXXXXXX", "flags": ["@css/root", "@warcraft/admin"] },
  "VipPlayer": { "identity": "76561198YYYYYYYYY", "flags": ["@warcraft/vip"] }
}
```

`@warcraft/admin` is required for the Warcraft admin commands; list it
explicitly even for `@css/root` admins.

## 6. Verify

From the server console:

```text
css_plugins list      → eight Warcraft.* plugins loaded
css_wc_status         → Core version, modules, persistence provider, race health
```

`css_wc_status` must show `Persistence: warcraft.mysql` and `Health: OK`.
Then walk through [SERVER_TEST_CHECKLIST.md](SERVER_TEST_CHECKLIST.md).

## Player commands

| Command | Purpose |
|---|---|
| `!wc` | main menu (race, abilities, stats, shop, VIP) |
| `!races` | race selection |
| `!ultimate` / `bind x css_ultimate` | activate the race ultimate |
| `!ability 1` / `bind c "css_ability 1"` | activate active ability in slot 1 |
| `!shop` | item shop |
| `!vip` | VIP status |
