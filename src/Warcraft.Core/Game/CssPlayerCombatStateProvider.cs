using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Warcraft.Core.Conditions;

namespace Warcraft.Core.Game;

/// <summary>
/// Reads combat facts from CS2 entities. Must only be used on the game thread.
/// </summary>
internal sealed class CssPlayerCombatStateProvider : IPlayerCombatStateProvider
{
    private const byte LifeAlive = 0;

    public PlayerCombatState? Get(ulong steamId)
    {
        var controller = Utilities.GetPlayerFromSteamId(steamId);
        if (controller is not { IsValid: true })
            return null;

        var pawn = controller.PlayerPawn.Value;
        if (pawn is not { IsValid: true })
            return new PlayerCombatState(false, 0, 0, false, null, null);

        var weapon = pawn.WeaponServices?.ActiveWeapon.Value;
        string? designerName = null;
        string? category = null;

        if (weapon is { IsValid: true })
        {
            designerName = weapon.DesignerName;
            category = ToCategory(weapon.As<CCSWeaponBase>().VData?.WeaponType);
        }

        return new PlayerCombatState(
            pawn.LifeState == LifeAlive && pawn.Health > 0,
            pawn.Health,
            pawn.MaxHealth,
            (pawn.Flags & (uint)PlayerFlags.FL_ONGROUND) != 0,
            designerName,
            category);
    }

    private static string? ToCategory(CSWeaponType? type)
        => type switch
        {
            CSWeaponType.WEAPONTYPE_KNIFE => WeaponCategories.Knife,
            CSWeaponType.WEAPONTYPE_PISTOL => WeaponCategories.Pistol,
            CSWeaponType.WEAPONTYPE_SUBMACHINEGUN => WeaponCategories.Smg,
            CSWeaponType.WEAPONTYPE_RIFLE => WeaponCategories.Rifle,
            CSWeaponType.WEAPONTYPE_SHOTGUN => WeaponCategories.Shotgun,
            CSWeaponType.WEAPONTYPE_SNIPER_RIFLE => WeaponCategories.Sniper,
            CSWeaponType.WEAPONTYPE_MACHINEGUN => WeaponCategories.MachineGun,
            CSWeaponType.WEAPONTYPE_TASER => WeaponCategories.Taser,
            CSWeaponType.WEAPONTYPE_GRENADE => WeaponCategories.Grenade,
            CSWeaponType.WEAPONTYPE_C4 => WeaponCategories.C4,
            CSWeaponType.WEAPONTYPE_EQUIPMENT or
                CSWeaponType.WEAPONTYPE_STACKABLEITEM => WeaponCategories.Equipment,
            _ => null
        };
}
