using Warcraft.Api.Events;

namespace Warcraft.Core.Events;

internal sealed class WarcraftEventBus : IWarcraftEventBus
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = [];
    private readonly object _sync = new();

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
        where TEvent : IWarcraftEvent
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_sync)
        {
            if (!_handlers.TryGetValue(typeof(TEvent), out var handlers))
            {
                handlers = [];
                _handlers[typeof(TEvent)] = handlers;
            }

            handlers.Add(handler);
        }

        return new Subscription(() => Unsubscribe(handler));
    }

    public void Publish<TEvent>(TEvent @event)
        where TEvent : IWarcraftEvent
    {
        Delegate[] snapshot;

        lock (_sync)
            snapshot = _handlers.TryGetValue(typeof(TEvent), out var handlers)
                ? handlers.ToArray()
                : [];

        foreach (var handler in snapshot.Cast<Action<TEvent>>())
            handler(@event);
    }

    private void Unsubscribe<TEvent>(Action<TEvent> handler)
        where TEvent : IWarcraftEvent
    {
        lock (_sync)
        {
            if (!_handlers.TryGetValue(typeof(TEvent), out var handlers))
                return;

            handlers.Remove(handler);
            if (handlers.Count == 0)
                _handlers.Remove(typeof(TEvent));
        }
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
