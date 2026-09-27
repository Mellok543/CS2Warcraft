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
- `Warcraft.Achievements` — achievements and race mastery (progress stored by Core).

No feature module may directly mutate another module or player persistence.

## Documentation

| Document | Contents |
|---|---|
| [INSTALL.md](docs/INSTALL.md) | server requirements, drop-in layout, database, admins, verification |
| [CONFIGURATION.md](docs/CONFIGURATION.md) | every config file and key |
| [ADMIN_COMMANDS.md](docs/ADMIN_COMMANDS.md) | admin commands and `css_wc_status` |
| [UPGRADE.md](docs/UPGRADE.md) | upgrades, config/race updates, hot reload rules, rollback |
| [SERVER_TEST_CHECKLIST.md](docs/SERVER_TEST_CHECKLIST.md) | release smoke test on a real server |
| [ARCHITECTURE.md](docs/ARCHITECTURE.md) | module rules, threading, event flow |
| [RACES.md](docs/RACES.md) / [ABILITIES.md](docs/ABILITIES.md) | race JSON, unlock tree, mechanics |
| [DATABASE.md](docs/DATABASE.md), [VIP.md](docs/VIP.md), [SHOP.md](docs/SHOP.md) | module details |

## Quick start

```bash
dotnet build CS2Warcraft.slnx -c Release
```

Copy the contents of `artifacts/addons/counterstrikesharp/` (or the
`cs2warcraft-<version>` CI artifact) into `game/csgo/addons/counterstrikesharp/`.
`Warcraft.Api.dll` lives only in `shared/Warcraft.Api/`. Details:
[docs/INSTALL.md](docs/INSTALL.md).
