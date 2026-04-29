namespace SmartHMI.Core.Interfaces;

public interface IEventBus
{
    void Subscribe<TEvent>(Action<TEvent> handler);
    void Unsubscribe<TEvent>(Action<TEvent> handler);
    void Publish<TEvent>(TEvent evt);
}
