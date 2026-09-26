namespace Warcraft.Core.Conditions;

/// <summary>
/// Game-state facts about one player, captured on the game thread and passed to
/// ability conditions. Contains no engine objects.
/// </summary>
internal readonly record struct PlayerCombatState(
    bool IsAlive,
    int Health,
    int MaxHealth,
    bool IsGrounded,
    string? WeaponDesignerName,
    string? WeaponCategory)
{
    public double HealthFraction
        => MaxHealth <= 0 ? 0.0 : Math.Clamp((double)Health / MaxHealth, 0.0, 1.0);
}

internal interface IPlayerCombatStateProvider
{
    PlayerCombatState? Get(ulong steamId);
}
