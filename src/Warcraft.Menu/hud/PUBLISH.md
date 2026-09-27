# CS2Warcraft Panorama UI

Warcraft.Menu uses a real Panorama HUD based on XML/CSS and custom_hud_layout.

## Local build

Run from the repository root:

    powershell -ExecutionPolicy Bypass -File .\src\Warcraft.Menu\hud\build.ps1 -Cs2 "E:\Steam\steamapps\common\Counter-Strike Global Offensive"

Default Workshop Tools addon name: cs2warcraft_ui

The script compiles warcraft_menu.xml and warcraft_menu.css and also installs local loose Panorama resources unless -NoLocalInstall is specified.

Restart the CS2 client after rebuilding because Panorama resources are cached for the session.

The same script also copies `assets/addon` (ability models: totems, aura ring,
shield bubble, entangle roots) into the addon and compiles every `.vmdl`; see
[assets/README.md](../../../assets/README.md).

## Server delivery

Publish one stable Workshop addon for the Warcraft UI and distribute it to clients as a client-only addon through MultiAddonManager.

Do not create a new Workshop item for each UI revision; update the existing item.