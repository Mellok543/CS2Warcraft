using Warcraft.Api.Abilities;
using Warcraft.Api.Events;
using Warcraft.Core.Events;

namespace Warcraft.Core.Tests;

public sealed class EventBusTests
{
    [Fact]
    public void PublishingThroughBaseTypeReachesConcreteSubscribers()
    {
        var bus = new WarcraftEventBus();
        var received = 0;
        bus.Subscribe<UltimatePressedEvent>(_ => received++);

        AbilityActivationEvent @event = new UltimatePressedEvent
        {
            SteamId = 1,
            Ability = new PlayerAbilitySnapshot("x", "race", 1, 1, default, default)
        };

        bus.Publish(@event);

        Assert.Equal(1, received);
    }

    [Fact]
    public void DisposedSubscriptionStopsReceivingAndFaultsAreIsolated()
    {
        var errors = new List<Exception>();
        var bus = new WarcraftEventBus(errors.Add);
        var received = 0;

        bus.Subscribe<RoundStartEvent>(_ => throw new InvalidOperationException("boom"));
        var subscription = bus.Subscribe<RoundStartEvent>(_ => received++);

        bus.Publish(new RoundStartEvent());
        subscription.Dispose();
        bus.Publish(new RoundStartEvent());

        Assert.Equal(1, received);
        Assert.Equal(2, errors.Count);
    }
}
