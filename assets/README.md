# CS2Warcraft visual assets

`assets/addon/` mirrors the **content root** of the Warcraft UI Workshop addon
(`cs2warcraft_ui`). It contains ModelDoc sources for the ability models the
server spawns (`src/Warcraft.Abilities/Game/WarcraftModels.cs`):

| Model | Used by | Source |
|---|---|---|
| `models/warcraft/totems/totem_healing` | `healing_totem` | Meshy text-to-3D |
| `models/warcraft/totems/totem_flame` | `flame_totem` | Meshy |
| `models/warcraft/totems/totem_frost` | `frost_totem` | Meshy |
| `models/warcraft/totems/totem_war` | `war_totem` | Meshy |
| `models/warcraft/totems/totem_shield` | `shield_totem` | Meshy |
| `models/warcraft/effects/entangle_roots` | `entangle` (at the rooted target) | Meshy |
| `models/warcraft/effects/aura_ring` | every aura (tinted per aura, follows the owner) | generated |
| `models/warcraft/effects/shield_bubble` | `divine_shield` (follows the caster) | generated |

Totems are 64 units tall, the roots ring 72 units wide. Aura ring and shield
bubble use a translucent, self-illuminated `csgo_complex` material and are
tinted through the entity render color.

## Build and publish

```powershell
powershell -ExecutionPolicy Bypass -File .\src\Warcraft.Menu\hud\build.ps1 -Cs2 "E:\Steam\steamapps\common\Counter-Strike Global Offensive"
```

The script copies `assets/addon/*` into `content/csgo_addons/cs2warcraft_ui/`,
compiles the Panorama HUD and every `models/warcraft/**/*.vmdl`, then fails if
any compiled file is missing. Upload the compiled addon from the Workshop Tools
as usual (see `src/Warcraft.Menu/hud/PUBLISH.md`).

Server and clients must both mount the addon (MultiAddonManager). If the
server runs without it, set `configs/warcraft/visuals.json` → `"models": false`:
totems then fall back to beam pillars and aura rings/shields are not drawn.

## Regenerating models

```bash
# 1. Generate (≈30 Meshy credits per model; resumable via meshy_state.json)
MESHY_API_KEY=... python assets/tools/meshy_generate.py <work_dir>

# 2. Inspect: textured front/side preview PNG for each OBJ
python assets/tools/preview_obj.py <work_dir>/totem_war/model.obj <work_dir>/totem_war/texture.png preview.png

# 3. Normalize into assets/addon (scale, ground, Y-up, 1024px textures, vmdl/vmat)
python assets/tools/prepare_models.py <work_dir>
```

Never commit the API key. `prepare_models.py` keeps OBJ Y-up on purpose:
ModelDoc imports OBJ as engine `(x, y, z) = obj (z, x, y)` at scale 1
(verified by compiling a test box and reading its bounds from the `.vmdl_c`).
Models whose generated shape stands although it must lie on the ground are
listed in `LAY_FLAT`.
