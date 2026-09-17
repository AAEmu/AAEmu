using System.Reflection;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// Diffuse a cast: value1 is the chance in percent, and 0/100 are both "take the effect" — special effect
/// 20539 carries 0 on skill 31183 샤티곤 군단의 전투함성, whose own text says it always clears the enemies'
/// casting.
/// </summary>
[NotInParallel]
public class CastInterruptRulesTests
{
    private SingletonScope<TaskManager> _tasks;

    [Before(Test)]
    public void Setup() =>
        // A real, never started task manager, so Task.Cancel() has a queue to remove the cast from.
        _tasks = new SingletonScope<TaskManager>(new TaskManager(Mock.Of<ITickManager>().Object));

    [After(Test)]
    public void Teardown() => _tasks.Dispose();

    [Test]
    [Arguments(0, 0)]
    [Arguments(0, 99)]
    [Arguments(100, 0)]
    [Arguments(100, 99)]
    [Arguments(150, 99)]
    public async Task NoChanceGate_AlwaysTakes(int chancePercent, int rollPercent)
    {
        await Assert.That(CastInterruptRules.RollSucceeds(chancePercent, rollPercent)).IsTrue();
    }

    [Test]
    public async Task NegativeChance_IsAlsoNoGate()
    {
        await Assert.That(CastInterruptRules.RollSucceeds(-1, 99)).IsTrue();
    }

    [Test]
    public async Task APercentChance_RollsAgainstIt()
    {
        // 10 is special effect 997 (skill 12390 로우킥), 1 is 3799 (skill 15537).
        await Assert.That(CastInterruptRules.RollSucceeds(10, 9)).IsTrue();
        await Assert.That(CastInterruptRules.RollSucceeds(10, 10)).IsFalse();
        await Assert.That(CastInterruptRules.RollSucceeds(1, 0)).IsTrue();
        await Assert.That(CastInterruptRules.RollSucceeds(1, 1)).IsFalse();
    }

    [Test]
    public async Task NoSkillTask_ThereIsNothingToInterrupt()
    {
        var unit = new Unit { ObjId = 1 };

        await Assert.That(CastInterruptRules.TryInterrupt(unit)).IsFalse();
        await Assert.That(CastInterruptRules.TryInterrupt(null)).IsFalse();
    }

    [Test]
    public async Task ACastInFlight_IsStoppedAndCleared()
    {
        var unit = new Unit { ObjId = 5 };
        var skill = new Skill(new SkillTemplate { Id = 11051 });
        unit.SkillTask = new CastTask(skill, unit, null, null, null, null);

        await Assert.That(CastInterruptRules.TryInterrupt(unit)).IsTrue();

        // Cancelled is what CastTask.Execute checks before Cast(), and Stop() is what clears the unit.
        await Assert.That(skill.Cancelled).IsTrue();
        await Assert.That(unit.SkillTask).IsNull();
    }

    private sealed class SingletonScope<T> : IDisposable where T : class
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
}
