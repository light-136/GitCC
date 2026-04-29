using SmartHMI.Core.Interfaces;

namespace SmartHMI.Core.EventBus;

public class EventAggregator : IEventBus
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = new();
    private readonly Lock _lock = new();

    public void Subscribe<TEvent>(Action<TEvent> handler)
    {
        lock (_lock)
        {
            var type = typeof(TEvent);
            if (!_handlers.TryGetValue(type, out var list))
            {
                list = new List<Delegate>();
                _handlers[type] = list;
            }
            list.Add(handler);
        }
    }

    public void Unsubscribe<TEvent>(Action<TEvent> handler)
    {
        lock (_lock)
        {
            var type = typeof(TEvent);
            if (_handlers.TryGetValue(type, out var list))
                list.Remove(handler);
        }
    }

    public void Publish<TEvent>(TEvent evt)
    {
        List<Delegate> snapshot;
        lock (_lock)
        {
            if (!_handlers.TryGetValue(typeof(TEvent), out var list))
                return;
            snapshot = new List<Delegate>(list);
        }
        foreach (var handler in snapshot)
            ((Action<TEvent>)handler)(evt);
    }
}
