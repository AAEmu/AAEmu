using System.Reflection;

using AAEmu.Commons.Utils;

namespace AAEmu.UnitTests.Utils;

/// <summary>
/// Shared copy of the helper several test classes carry privately: installs <paramref name="value"/> as
/// <see cref="Singleton{T}"/>'s cached instance (<c>s_instance</c>) and restores the previous one on dispose,
/// so production code that reaches a manager through <c>Singleton&lt;T&gt;.Instance</c> can be driven from a
/// test. Used by the buff-modifier mocks, which call <c>BuffTemplate.Start</c>.
/// </summary>
internal sealed class SingletonScope<T> : IDisposable where T : class
{
    private readonly FieldInfo _field =
        typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
    private readonly object _previous;

    public SingletonScope(T value)
    {
        _previous = _field.GetValue(null);
        _field.SetValue(null, value);
    }

    public void Dispose() => _field.SetValue(null, _previous);
}
