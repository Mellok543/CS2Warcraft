using Warcraft.Api.Events;

namespace Warcraft.Core.Events;

/// <summary>
/// Synchronous in-process event bus. Events are dispatched by their runtime type,
/// so publishing through a base type (e.g. <see cref="AbilityActivationEvent"/>)
/// still reaches subscribers of the concrete event.
/// </summary>
internal sealed class WarcraftEventBus(Action<Exception>? errorHandler = null) : IWarcraftEventBus
{
    private readonly Dictionary<Type, List<Handler>> _handlers = [];
    private readonly object _sync = new();

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
        where TEvent : IWarcraftEvent
    {
        ArgumentNullException.ThrowIfNull(handler);

        var entry = new Handler(@event => handler((TEvent)@event));

        lock (_sync)
        {
            if (!_handlers.TryGetValue(typeof(TEvent), out var handlers))
            {
                handlers = [];
                _handlers[typeof(TEvent)] = handlers;
            }

            handlers.Add(entry);
        }

        return new Subscription(() => Unsubscribe(typeof(TEvent), entry));
    }

    public void Publish<TEvent>(TEvent @event)
        where TEvent : IWarcraftEvent
    {
        ArgumentNullException.ThrowIfNull(@event);

        Handler[] snapshot;

        lock (_sync)
            snapshot = _handlers.TryGetValue(@event.GetType(), out var handlers)
                ? handlers.ToArray()
                : [];

        foreach (var handler in snapshot)
        {
            try
            {
                handler.Invoke(@event);
            }
            catch (Exception exception)
            {
                errorHandler?.Invoke(exception);
            }
        }
    }

    private void Unsubscribe(Type eventType, Handler handler)
    {
        lock (_sync)
        {
            if (!_handlers.TryGetValue(eventType, out var handlers))
                return;

            handlers.Remove(handler);
            if (handlers.Count == 0)
                _handlers.Remove(eventType);
        }
    }

    private sealed class Handler(Action<IWarcraftEvent> invoke)
    {
        public void Invoke(IWarcraftEvent @event) => invoke(@event);
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
