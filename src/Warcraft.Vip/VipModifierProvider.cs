using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Entities;
using Warcraft.Api.Modifiers;

namespace Warcraft.Vip;

/// <summary>
/// VIP benefits expressed as Core modifiers. VIP never changes XP, races or the
/// shop itself: Core (and the shop through Core) apply these values.
/// </summary>
internal sealed class VipModifierProvider(VipConfig config) : IPlayerModifierProvider
{
    public const string Id = "warcraft.vip";

    private readonly PlayerModifiers _vip = new(
        Math.Max(0.0, config.XpMultiplier),
        Math.Max(0, config.BonusSkillPointsPerLevel),
        config.CanAccessVipRaces,
        Math.Max(0, config.ExtraRaceSlots),
        Math.Clamp(config.ShopDiscount, 0.0, 1.0));

    public string ProviderId => Id;

    public PlayerModifiers VipModifiers => _vip;

    public bool IsVip(ulong steamId)
        => steamId != 0 && AdminManager.PlayerHasPermissions(new SteamID(steamId), [config.Permission]);

    public PlayerModifiers GetModifiers(ulong steamId)
        => IsVip(steamId) ? _vip : PlayerModifiers.Default;
}
