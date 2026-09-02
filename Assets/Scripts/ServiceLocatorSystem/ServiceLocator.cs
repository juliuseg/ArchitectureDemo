using System;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

public static class ServiceLocator
{
    static readonly Dictionary<Type, object> services = new();

    public static void Register<T>(T service)
    {
        services[typeof(T)] = service!;
    }

    public static T Get<T>()
    {
        if (services.TryGetValue(typeof(T), out var service))
            return (T)service;

        throw new InvalidOperationException($"Service of type {typeof(T).Name} is not registered.");
    }

    public static bool TryGet<T>(out T service)
    {
        if (services.TryGetValue(typeof(T), out var obj))
        {
            service = (T)obj;
            return true;
        }

        service = default!;
        return false;
    }

    public static void Unregister<T>()
    {
        services.Remove(typeof(T));
    }

    public static void Clear()
    {
        services.Clear();
    }

#if UNITY_EDITOR
    [InitializeOnLoadMethod]
    static void RegisterPlayModeCleanup()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            Clear();
        }
    }
#endif
}
