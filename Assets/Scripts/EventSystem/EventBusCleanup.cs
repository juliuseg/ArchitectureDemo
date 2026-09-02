#if UNITY_EDITOR
using UnityEditor;
using System;
using System.Linq;
using System.Reflection;

[InitializeOnLoad]
public static class EventBusCleanup
{
    static EventBusCleanup()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            ClearAllBuses();
        }
    }

    private static void ClearAllBuses()
    {
        var eventTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .Where(t => typeof(IEvent).IsAssignableFrom(t) && !t.IsInterface);

        foreach (var eventType in eventTypes)
        {
            var busType = typeof(EventBus<>).MakeGenericType(eventType);
            busType.GetMethod("Clear")?.Invoke(null, null);
        }
    }
}
#endif