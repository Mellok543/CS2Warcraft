using Warcraft.Api.Abilities;

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
public sealed record PlayerKillEvent(
    ulong KillerSteamId,
    ulong VictimSteamId,
    bool Headshot,
    bool TeamKill) : IWarcraftEvent;
public sealed record PlayerHurtEvent(ulong VictimSteamId, ulong? AttackerSteamId, float Damage) : IWarcraftEvent;
public sealed record WeaponFireEvent(ulong SteamId, string Weapon) : IWarcraftEvent;
public sealed record PlayerJumpEvent(ulong SteamId) : IWarcraftEvent;
public sealed record RoundStartEvent() : IWarcraftEvent;
public sealed record RoundEndEvent(int WinnerTeam) : IWarcraftEvent;

/// <summary>
/// Published by Core on the game thread at the interval configured in core.json
/// (<c>gameTickIntervalMilliseconds</c>). Used by continuous passive abilities.
/// </summary>
public sealed record GameTickEvent(double ServerTime, TimeSpan Interval) : IWarcraftEvent;

public enum AbilityActivationOutcome
{
    NotHandled,
    Succeeded,
    Failed
}

/// <summary>
/// Base type of player-triggered activations. Core publishes it only after it has
/// verified the active race, learned level, unlockLevel, conditions and cooldown.
/// The handler that owns <see cref="AbilityId"/> performs the mechanic and reports
/// the outcome; Core then starts the cooldown centrally when the outcome is
/// <see cref="AbilityActivationOutcome.Succeeded"/>.
/// </summary>
public abstract class AbilityActivationEvent : IWarcraftEvent
{
    public required ulong SteamId { get; init; }
    public required PlayerAbilitySnapshot Ability { get; init; }

    public string AbilityId => Ability.AbilityId;
    public AbilityActivationOutcome Outcome { get; private set; }
    public string? Message { get; private set; }

    public bool IsFor(string abilityId)
        => Outcome == AbilityActivationOutcome.NotHandled &&
           string.Equals(AbilityId, abilityId, StringComparison.OrdinalIgnoreCase);

    public void Succeed(string? message = null)
        => Complete(AbilityActivationOutcome.Succeeded, message);

    public void Fail(string message)
        => Complete(AbilityActivationOutcome.Failed, message);

    private void Complete(AbilityActivationOutcome outcome, string? message)
    {
        if (Outcome != AbilityActivationOutcome.NotHandled)
            return;

        Outcome = outcome;
        Message = message;
    }
}

/// <summary>Activation of a non-ultimate active ability (<c>css_ability [slot]</c>).</summary>
public sealed class AbilityPressedEvent : AbilityActivationEvent
{
    public required int Slot { get; init; }
}

/// <summary>Activation of the active race ultimate (<c>css_ultimate</c>).</summary>
public sealed class UltimatePressedEvent : AbilityActivationEvent
{
}

/// <summary>Published after a successful activation and cooldown start.</summary>
public sealed record AbilityActivatedEvent(
    ulong SteamId,
    string AbilityId,
    bool IsUltimate,
    DateTimeOffset? ReadyAt) : IWarcraftEvent;

public sealed record PlayerStateChangedEvent(
    ulong SteamId,
    string Reason) : IWarcraftEvent;

public sealed record RaceReloadRequestedEvent(string RequestedBy) : IWarcraftEvent;

public sealed record RaceCatalogReloadedEvent(
    bool Success,
    int RaceCount,
    IReadOnlyList<string> Errors,
    string Source) : IWarcraftEvent;

public sealed class DamagePreEvent : IWarcraftEvent
{
    public required ulong VictimSteamId { get; init; }
    public ulong? AttackerSteamId { get; init; }

    /// <summary>True when the damage was dealt by any player pawn (human or bot).</summary>
    public bool AttackerIsPlayer { get; init; }

    public required float Damage { get; set; }
    public string? Weapon { get; init; }
}

public sealed record DamagePostEvent(
    ulong VictimSteamId,
    ulong? AttackerSteamId,
    float FinalDamage,
    string? Weapon) : IWarcraftEvent;
