using System.Reflection;

namespace AAEmu.UnitTests.Utils;

/// <summary>
/// Replaces a <see cref="AAEmu.Commons.Utils.Singleton{T}"/>'s instance for the duration of a scope and puts
/// the previous one back on dispose. Nine test classes carry a private copy of this; it is shared here
/// because a production change that adds a singleton dependency to a shared path (as
/// <c>BuffTemplate.Start</c> did when buff grants landed) otherwise breaks every test that reaches that
/// path without scoping the manager itself.
/// </summary>
public sealed class SingletonScope<T> : IDisposable where T : class
{
    private static readonly FieldInfo InstanceField = typeof(AAEmu.Commons.Utils.Singleton<T>)
        .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;

    private readonly object _previous;

    public SingletonScope(T value)
    {
        _previous = InstanceField.GetValue(null);
        InstanceField.SetValue(null, value);
    }

    public void Dispose() => InstanceField.SetValue(null, _previous);
}