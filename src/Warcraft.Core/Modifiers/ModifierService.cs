using Warcraft.Api.Modifiers;

namespace Warcraft.Core.Modifiers;

internal sealed class ModifierService : IModifiersApi
{
    private readonly Dictionary<string, IPlayerModifierProvider> _providers =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private readonly Dictionary<(ulong SteamId, string Source), TemporaryXpMultiplier> _temporaryXp = [];

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
        var now = DateTimeOffset.UtcNow;

        lock (_sync)
        {
            foreach (var key in _temporaryXp.Where(x => x.Value.ExpiresAt <= now).Select(x => x.Key).ToArray())
                _temporaryXp.Remove(key);

            foreach (var boost in _temporaryXp.Where(x => x.Key.SteamId == steamId).Select(x => x.Value))
                xpMultiplier *= Math.Max(0.0, boost.Multiplier);
        }
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
    public void AddTemporaryXpMultiplier(ulong steamId, double multiplier, TimeSpan duration, string source)
    {
        if (steamId == 0 || multiplier <= 0 || duration <= TimeSpan.Zero || string.IsNullOrWhiteSpace(source))
            return;

        lock (_sync)
        {
            var key = (steamId, source);
            var now = DateTimeOffset.UtcNow;
            var expiresAt = now + duration;

            if (_temporaryXp.TryGetValue(key, out var existing) && existing.ExpiresAt > now)
                expiresAt = existing.ExpiresAt + duration;

            _temporaryXp[key] = new TemporaryXpMultiplier(multiplier, expiresAt);
        }
    }

    public TimeSpan? GetTemporaryXpMultiplierRemaining(ulong steamId, string source)
    {
        lock (_sync)
        {
            var key = (steamId, source);
            if (!_temporaryXp.TryGetValue(key, out var value))
                return null;

            var remaining = value.ExpiresAt - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                _temporaryXp.Remove(key);
                return null;
            }

            return remaining;
        }
    }

    private sealed record TemporaryXpMultiplier(double Multiplier, DateTimeOffset ExpiresAt);
}
