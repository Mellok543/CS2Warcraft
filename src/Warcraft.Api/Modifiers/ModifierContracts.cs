namespace Warcraft.Api.Modifiers;

public interface IModifiersApi
{
    bool RegisterProvider(IPlayerModifierProvider provider);
    bool UnregisterProvider(string providerId);
    PlayerModifiers GetCombined(ulong steamId);
    void AddTemporaryXpMultiplier(ulong steamId, double multiplier, TimeSpan duration, string source);
    TimeSpan? GetTemporaryXpMultiplierRemaining(ulong steamId, string source);
}

public interface IPlayerModifierProvider
{
    string ProviderId { get; }
    PlayerModifiers GetModifiers(ulong steamId);
}

public sealed record PlayerModifiers(
    double XpMultiplier = 1.0,
    int BonusSkillPointsPerLevel = 0,
    bool CanAccessVipRaces = false,
    int ExtraRaceSlots = 0,
    double ShopDiscount = 0.0)
{
    public static PlayerModifiers Default { get; } = new();
}
