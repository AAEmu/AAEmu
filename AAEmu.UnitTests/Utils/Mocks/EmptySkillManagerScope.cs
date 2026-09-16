using System.Reflection;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.UnitTests.Utils.Mocks;

/// <summary>
/// Installs a <c>SkillManager</c> whose buff tag tables are empty for the length of a test, and puts
/// whatever was installed before back when it is disposed.
/// <para>
/// Applying a bonus row goes through <see cref="BuffTemplate.Start"/>, which asks the manager for the
/// buff tag tables (the NoFight and Returning tags). A test that only wants the modifier plumbing still
/// needs the singleton to answer, and without an instance the singleton guard throws instead — so the test
/// that applies rows installs this, exactly as the damage-multiplier tests next door do.
/// </para>
/// </summary>
public sealed class EmptySkillManagerScope : IDisposable
{
    private static readonly FieldInfo InstanceField =
        typeof(Singleton<SkillManager>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;

    private readonly object _previous;

    public EmptySkillManagerScope()
    {
        var skillManager = new SkillManager(Mock.Of<IAnimationManager>().Object, Mock.Of<IPlotManager>().Object);
        SetField(skillManager, "_buffTags", new Dictionary<uint, List<uint>>());
        _previous = InstanceField.GetValue(null);
        InstanceField.SetValue(null, skillManager);
    }

    public void Dispose() => InstanceField.SetValue(null, _previous);

    private static void SetField(object target, string name, object value)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                continue;
            field.SetValue(target, value);
            return;
        }

        throw new InvalidOperationException($"Missing field {name}");
    }
}
