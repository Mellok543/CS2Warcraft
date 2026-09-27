using Warcraft.Abilities.Game;
using Warcraft.Api;
using Warcraft.Api.Abilities;
using Warcraft.Api.Events;

namespace Warcraft.Abilities;

internal interface IAbilityHandler : IDisposable
{
    string Id { get; }
    void UseEffects(EffectKit effects);
    void Register(IWarcraftApi api);
}

/// <summary>
/// Registration and subscription lifecycle shared by all handlers. A handler
/// implements one mechanic, subscribes only to Warcraft.Api events and reads all
/// numbers from the race-specific <see cref="PlayerAbilitySnapshot"/>.
/// </summary>
internal abstract class AbilityHandler : IAbilityHandler
{
    protected const string OwnerModule = "warcraft.abilities";

    private readonly List<IDisposable> _subscriptions = [];

    public abstract string Id { get; }
    protected abstract AbilityKind Kind { get; }
    protected abstract string Description { get; }
    protected virtual string? DisplayName => null;

    protected IWarcraftApi? Api { get; private set; }

    /// <summary>Shared visuals; set by the plugin before <see cref="Register"/>, null in isolation.</summary>
    protected EffectKit? Fx { get; private set; }

    public void UseEffects(EffectKit effects) => Fx = effects;

    public void Register(IWarcraftApi api)
    {
        var result = api.Abilities.Register(new AbilityRegistration(
            Id,
            OwnerModule,
            Kind,
            Description,
            DisplayName));

        if (!result.Success)
            throw new InvalidOperationException(result.Message);

        Api = api;
        Subscribe(api.Events);
    }

    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
            subscription.Dispose();

        _subscriptions.Clear();
        OnDisposed();

        Api?.Abilities.Unregister(Id, OwnerModule);
        Api = null;
    }

    protected abstract void Subscribe(IWarcraftEventBus events);

    protected void Track(IDisposable subscription) => _subscriptions.Add(subscription);

    protected virtual void OnDisposed()
    {
    }

    /// <summary>Learned, unlocked and conditions satisfied — resolved by Core.</summary>
    protected PlayerAbilitySnapshot? GetUsable(ulong steamId)
        => Api?.Abilities.GetUsableAbility(steamId, Id);
}
