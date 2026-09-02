using System;

public static class EventBus<T> where T : IEvent
{
    private static event Action<T> OnEvent;

    public static void Register(Action<T> handler) => OnEvent += handler;
    public static void Deregister(Action<T> handler) => OnEvent -= handler;
    public static void Raise(T @event) => OnEvent?.Invoke(@event);

    public static void Clear() => OnEvent = null;
}