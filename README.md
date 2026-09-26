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
