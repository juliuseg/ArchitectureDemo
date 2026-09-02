using UnityEngine;

/// <summary>
/// Base class for MonoBehaviour services. Auto-unregisters itself in OnDestroy.
/// </summary>
public abstract class ServiceUser<T> : MonoBehaviour
{
    protected virtual void OnDestroy()
    {
        if (ServiceLocator.TryGet<T>(out var service) && ReferenceEquals(service, this))
        {
            ServiceLocator.Unregister<T>();
        }
    }
}