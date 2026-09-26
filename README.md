# CS2Warcraft

A standalone Warcraft-style CS2 server project built with CounterStrikeSharp, C# and .NET 10.

This repository is completely independent from JBForsaken/Jailbreak.

## Architecture

The project uses a contract-first modular architecture:

- `Warcraft.Api` — contracts, DTOs and capability names only.
- `Warcraft.Core` — the single owner of runtime state and orchestration.
- `Warcraft.Database` — persistence provider (MySQL).
- `Warcraft.Races` — JSON race loading/hot reload.
- `Warcraft.Abilities` — reusable ability handlers.
- `Warcraft.Menu` — shared Warcraft UI/menu.
- `Warcraft.Vip` — VIP modifiers registered through Core.
- `Warcraft.Admin` — admin commands through Core API.
- `Warcraft.Shop` — shop integration through Core API.

No feature module may directly mutate another module or player persistence.

## Installation

```bash
dotnet build CS2Warcraft.slnx -c Release
```

The Release build stages a drop-in layout in `artifacts/addons/counterstrikesharp/`
(CI publishes the same folder as the `cs2warcraft-counterstrikesharp` artifact).
Copy its contents into `game/csgo/addons/counterstrikesharp/` on the server:

```text
shared/Warcraft.Api/Warcraft.Api.dll        ← shared contracts, loaded once for all plugins
plugins/Warcraft.Core/
plugins/Warcraft.Database/                  ← includes MySqlConnector.dll
plugins/Warcraft.Races/                     ← includes defaults/races/*.json
plugins/Warcraft.Abilities/
plugins/Warcraft.Menu/
plugins/Warcraft.Admin/
plugins/Warcraft.Vip/                       ← VIP modifiers (docs/VIP.md)
plugins/Warcraft.Shop/                      ← item shop (docs/SHOP.md)
```

Do not copy `bin/` folders directly: `Warcraft.Api.dll` must exist only in
`shared/`, otherwise each plugin gets its own copy of `IWarcraftApi` and the
`warcraft:core` capability cannot be resolved.

Player commands: `!wc` (menu), `!shop`, `!vip`; binds: `bind x css_ultimate`,
`bind c "css_ability 1"`. New shipped race JSON files are copied only into an
empty races directory; copy them manually on existing servers.
