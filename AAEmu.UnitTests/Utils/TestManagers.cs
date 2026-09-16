using System.Reflection;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;

namespace AAEmu.UnitTests.Utils;

/// <summary>
/// The empty managers the shared mock helpers need, registered before the test run starts.
/// <c>BuffTemplate.Start</c> reads <c>SkillManager.Instance</c> for the skills a buff grants (#1594) and
/// <c>SkillManager</c> has no parameterless constructor to fall back on, so a helper that puts a
/// <c>unit_modifiers</c> row on a unit through <c>Start</c> needs one of these installed.
/// </summary>
/// <remarks>
/// Both halves below are load-bearing, measured on the merged tree:
/// <list type="bullet">
/// <item><description>
/// The assembly hook registers the manager before the first test. The private <see cref="SingletonScope{T}"/>
/// copies the buff-trigger tests hold for the length of their tests restore whatever they captured when they
/// end, and with nothing registered that is null — so before this hook, a restore landing between a helper's
/// install and its <c>Start</c> call took the manager away again and the helper threw the same
/// <c>InvalidOperationException</c> it exists to avoid (6 failures across 8 suite runs). With a manager live
/// from the start, those scopes capture and restore a real one instead.
/// </description></item>
/// <item><description>
/// The per-call <see cref="SingletonScope{T}"/> in each helper is still needed, because the field can be null
/// when a helper runs: with the hook alone, <c>CombatResourceCeilingTests.ACharacterSeesTheSameCeiling</c> —
/// a <c>[NotInParallel]</c> class, which runs after the parallel ones have unwound — failed on every run.
/// </description></item>
/// </list>
/// </remarks>
internal static class TestManagers
{
    [Before(HookType.Assembly)]
    public static void InstallSkillManager() =>
        typeof(Singleton<SkillManager>)
            .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!
            .SetValue(null, CreateSkillManager());

    /// <summary>
    /// An empty <see cref="SkillManager"/>: every lookup answers "no row", which is what a buff row built by
    /// hand in a test needs.
    /// </summary>
    internal static SkillManager CreateSkillManager() =>
        new(Mock.Of<IAnimationManager>().Object, Mock.Of<IPlotManager>().Object);
}
