namespace Warcraft.Abilities.Game;

/// <summary>
/// Model paths shipped in the Warcraft UI Workshop addon (sources in assets/addon,
/// compiled by src/Warcraft.Menu/hud/build.ps1). Precached on map load.
/// </summary>
internal static class WarcraftModels
{
    public const string TotemHealing = "models/warcraft/totems/totem_healing/totem_healing.vmdl";
    public const string TotemFlame = "models/warcraft/totems/totem_flame/totem_flame.vmdl";
    public const string TotemFrost = "models/warcraft/totems/totem_frost/totem_frost.vmdl";
    public const string TotemWar = "models/warcraft/totems/totem_war/totem_war.vmdl";
    public const string TotemShield = "models/warcraft/totems/totem_shield/totem_shield.vmdl";

    public const string EntangleRoots = "models/warcraft/effects/entangle_roots/entangle_roots.vmdl";
    public const string AuraRing = "models/warcraft/effects/aura_ring/aura_ring.vmdl";
    public const string ShieldBubble = "models/warcraft/effects/shield_bubble/shield_bubble.vmdl";

    public static IReadOnlyList<string> All { get; } =
    [
        TotemHealing, TotemFlame, TotemFrost, TotemWar, TotemShield,
        EntangleRoots, AuraRing, ShieldBubble
    ];
}
