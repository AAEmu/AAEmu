using System.Reflection;

namespace AAEmu.UnitTests.Utils;

/// <summary>
/// Replaces a <see cref="AAEmu.Commons.Utils.Singleton{T}"/>'s instance for the duration of a scope and puts
/// the previous one back on dispose. Nine test classes carry a private copy of this; it is shared here
/// because a production change that adds a singleton dependency to a shared path (as
/// <c>BuffTemplate.Start</c> did when buff grants landed) otherwise breaks every test that reaches that
/// path without scoping the manager itself.
/// </summary>
/// <remarks>
/// Disposing only restores when this scope's value is still the installed one. Scopes nest — a helper scopes
/// a manager inside a test that scoped its own — and when they are disposed out of order the older scope used
/// to put its <em>previous</em> value back over the newer scope's, or <c>null</c> over a live one, which made
/// a parallel test see an uninitialised singleton mid-call. Five classes raced that way (~1 run in 3);
/// leaving the newer value in place instead is the same convention <c>System.Diagnostics.Activity</c> uses
/// for nested scopes.
/// </remarks>
public sealed class SingletonScope<T> : IDisposable where T : class
{
    private static readonly FieldInfo InstanceField = typeof(AAEmu.Commons.Utils.Singleton<T>)
        .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;

    private readonly object _previous;
    private readonly object _installed;
    private bool _disposed;

    public SingletonScope(T value)
    {
        _previous = InstanceField.GetValue(null);
        _installed = value;
        InstanceField.SetValue(null, value);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        // Only take back what this scope put there. If another scope (or the production initialiser) has
        // replaced it since, that value belongs to something still running.
        if (ReferenceEquals(InstanceField.GetValue(null), _installed))
            InstanceField.SetValue(null, _previous);
    }
}
