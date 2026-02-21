using System.Reflection;

using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.Commons.Utils;

#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable CA1000 // Do not declare static members on generic types

/// <summary>
/// Base class used for singletons
/// </summary>
/// <typeparam name="T">The class type</typeparam>
public abstract class Singleton<T> where T : class
{
    private static T _instance;

    /// <summary>
    /// Gets the instance of the singleton. Resolves from the DI container when available,
    /// caching the result so subsequent calls are free. Falls back to reflection when DI
    /// is not configured (e.g. unit tests).
    /// </summary>
    public static T Instance
    {
        get
        {
            if (_instance != null)
                return _instance;

            if (SingletonContainer.ServiceProvider?.GetService<T>() is { } fromDi)
            {
                lock (typeof(T))
                {
                    _instance ??= fromDi;
                }
                return _instance;
            }

            OnInit();
            return _instance;
        }
    }

    private static void OnInit()
    {
        if (_instance != null)
            return;
        lock (typeof(T))
        {
            if (_instance != null)
                return;
            _instance = typeof(T).InvokeMember(typeof(T).Name,
                BindingFlags.CreateInstance |
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic,
                null, null, null) as T;
        }
    }
}
