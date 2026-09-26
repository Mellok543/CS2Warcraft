namespace Warcraft.Core.Conditions;

/// <summary>Weapon category names accepted by the <c>weaponTypes</c> condition.</summary>
internal static class WeaponCategories
{
    public const string Knife = "knife";
    public const string Pistol = "pistol";
    public const string Smg = "smg";
    public const string Rifle = "rifle";
    public const string Shotgun = "shotgun";
    public const string Sniper = "sniper";
    public const string MachineGun = "machinegun";
    public const string Taser = "taser";
    public const string Grenade = "grenade";
    public const string C4 = "c4";
    public const string Equipment = "equipment";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(
        [Knife, Pistol, Smg, Rifle, Shotgun, Sniper, MachineGun, Taser, Grenade, C4, Equipment],
        StringComparer.OrdinalIgnoreCase);
}
