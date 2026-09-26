namespace Warcraft.Api.Events;

public interface IWarcraftEvent
{
}

public interface IWarcraftEventBus
{
    IDisposable Subscribe<TEvent>(Action<TEvent> handler)
        where TEvent : IWarcraftEvent;

    void Publish<TEvent>(TEvent @event)
        where TEvent : IWarcraftEvent;
}

public sealed record PlayerSpawnEvent(ulong SteamId) : IWarcraftEvent;
public sealed record PlayerDeathEvent(ulong SteamId, ulong? KillerSteamId) : IWarcraftEvent;
public sealed record PlayerKillEvent(ulong KillerSteamId, ulong VictimSteamId, bool Headshot) : IWarcraftEvent;
public sealed record PlayerHurtEvent(ulong VictimSteamId, ulong? AttackerSteamId, float Damage) : IWarcraftEvent;
public sealed record WeaponFireEvent(ulong SteamId, string Weapon) : IWarcraftEvent;
public sealed record PlayerJumpEvent(ulong SteamId) : IWarcraftEvent;
public sealed record RoundStartEvent() : IWarcraftEvent;
public sealed record RoundEndEvent(int WinnerTeam) : IWarcraftEvent;
public sealed record UltimatePressedEvent(ulong SteamId) : IWarcraftEvent;
public sealed record AbilityPressedEvent(ulong SteamId, string? AbilityId = null) : IWarcraftEvent;

public sealed class DamagePreEvent : IWarcraftEvent
{
    public required ulong VictimSteamId { get; init; }
    public ulong? AttackerSteamId { get; init; }
    public required float Damage { get; set; }
    public string? Weapon { get; init; }
}

public sealed record DamagePostEvent(
    ulong VictimSteamId,
    ulong? AttackerSteamId,
    float FinalDamage,
    string? Weapon) : IWarcraftEvent;
