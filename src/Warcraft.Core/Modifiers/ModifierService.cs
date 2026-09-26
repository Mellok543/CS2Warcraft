using Warcraft.Api.Modifiers;

namespace Warcraft.Core.Modifiers;

internal sealed class ModifierService : IModifiersApi
{
    private readonly Dictionary<string, IPlayerModifierProvider> _providers =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    public bool RegisterProvider(IPlayerModifierProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        lock (_sync)
            return _providers.TryAdd(provider.ProviderId, provider);
    }

    public bool UnregisterProvider(string providerId)
    {
        lock (_sync)
            return _providers.Remove(providerId);
    }

    public PlayerModifiers GetCombined(ulong steamId)
    {
        IPlayerModifierProvider[] providers;

        lock (_sync)
            providers = _providers.Values.ToArray();

        var xpMultiplier = 1.0;
        var bonusSkillPoints = 0;
        var vipRaceAccess = false;
        var extraRaceSlots = 0;
        var shopDiscount = 0.0;

        foreach (var provider in providers)
        {
            var value = provider.GetModifiers(steamId);
            xpMultiplier *= Math.Max(0.0, value.XpMultiplier);
            bonusSkillPoints += value.BonusSkillPointsPerLevel;
            vipRaceAccess |= value.CanAccessVipRaces;
            extraRaceSlots += value.ExtraRaceSlots;
            shopDiscount = Math.Max(shopDiscount, value.ShopDiscount);
        }

        return new(
            xpMultiplier,
            bonusSkillPoints,
            vipRaceAccess,
            extraRaceSlots,
            Math.Clamp(shopDiscount, 0.0, 1.0));
    }
}
