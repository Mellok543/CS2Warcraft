# Warcraft.Races

Runtime race directory:

```text
csgo/configs/warcraft/races/*.json
```

A new race requires only a new JSON file as long as every referenced ability ID
has an implementation in `Warcraft.Abilities`.

The loader watches JSON files and performs a debounced hot reload. It parses all
files first and only then asks Core to replace the active race catalog.

If parsing or Core validation fails, the previous catalog remains active.

Other modules can request a reload without referencing `Warcraft.Races`:

```csharp
api.Events.Publish(new RaceReloadRequestedEvent("warcraft.admin"));
```
