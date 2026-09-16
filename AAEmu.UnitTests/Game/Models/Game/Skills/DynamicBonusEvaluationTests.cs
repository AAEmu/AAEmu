using System.Reflection;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// Dynamic unit modifiers driven end to end: <see cref="BuffTemplate.Start"/> registers the bonus and
/// <see cref="Unit.CalculateWithBonuses"/> reads it. The rows quoted are shipped 10.0.2.13 rows; the
/// manager singletons are scoped so that Start resolves them the way a loaded manager does.
/// </summary>
/// <remarks>
/// Not in parallel: every test here swaps the same <see cref="Singleton{T}"/> instance that Start
/// resolves, so two of them at once would each see the other's manager.
/// </remarks>
[NotInParallel]
public class DynamicBonusEvaluationTests
{
    [Test]
    public async Task Start_FormulaFuncRow_AddsTheUnitsOwnAttributeValue()
    {
        // dynamic_unit_modifiers 734: buff 28981 "(attr_test_func) 6 max_health", unit_attribute_id 6
        // (max_health), unit_modifier_type_id 0 (Value), func_type FormulaFunc, func_id 14.
        // formula_funcs 14 = "attr_6".
        using var formulas = InstallFormulaFunc(14, "attr_6");
        var owner = new Unit { MaxHp = 500 };
        var template = new BuffTemplate { Id = 28981 };
        template.DynamicBonuses.Add(new DynamicBonusTemplate
        {
            Attribute = UnitAttribute.MaxHealth,
            ModifierType = UnitModifierType.Value,
            FuncId = 14,
            FuncType = DynamicBonusFuncRules.FormulaFuncType
        });

        StartBuff(template, owner, index: 7u);

        // attr_6 is the unit's max_health (500) and the row is a flat Value modifier on max_health,
        // so the accumulator gains exactly that: 0 -> 500, and 1000 -> 1500.
        await Assert.That(owner.CalculateWithBonuses(0d, UnitAttribute.MaxHealth)).IsEqualTo(500d);
        await Assert.That(owner.CalculateWithBonuses(1000d, UnitAttribute.MaxHealth)).IsEqualTo(1500d);
        await Assert.That(owner.GetDynamicBonuses(UnitAttribute.MaxHealth).Count).IsEqualTo(1);
    }

    [Test]
    public async Task Start_FormulaFuncPercentRow_AppliesTheClampedPercentOfTheAttribute()
    {
        // dynamic_unit_modifiers 955: buff 29238, unit_attribute_id 6 (max_health),
        // unit_modifier_type_id 1 (Percent), func_id 235.
        // formula_funcs 235 = "clamp ( -49/16000*gear_score + 62.25 , 1 , 50 )". A unit that is not a
        // character has no gear score, so gear_score binds 0 and clamp(62.25, 1, 50) is 50 percent.
        using var formulas = InstallFormulaFunc(235, "clamp ( -49/16000*gear_score + 62.25 , 1 , 50 )");
        var owner = new Unit { MaxHp = 500 };
        var template = new BuffTemplate { Id = 29238 };
        template.DynamicBonuses.Add(new DynamicBonusTemplate
        {
            Attribute = UnitAttribute.MaxHealth,
            ModifierType = UnitModifierType.Percent,
            FuncId = 235,
            FuncType = DynamicBonusFuncRules.FormulaFuncType
        });

        StartBuff(template, owner, index: 8u);

        await Assert.That(owner.CalculateWithBonuses(400d, UnitAttribute.MaxHealth)).IsEqualTo(600d);
    }

    [Test]
    public async Task Start_LinearFuncRow_KeepsInterpolatingWithTheBuffLifetime()
    {
        // dynamic_unit_modifiers 2: buff 114, unit_attribute_id 54 (melee_speed_mul), func_type
        // LinearFunc, func_id 1; linear_funcs 1 = start_value -250, end_value -600.
        var skills = new SkillManager(Mock.Of<IAnimationManager>().Object, Mock.Of<IPlotManager>().Object);
        SetField(skills, "_linearFuncs", new Dictionary<uint, LinearFuncTemplate>
        {
            [1] = new LinearFuncTemplate { Id = 1, StartValue = -250, EndValue = -600 }
        });
        using var skillScope = new SingletonScope<SkillManager>(skills);

        var owner = new Unit();
        var template = new BuffTemplate { Id = 114, Duration = 10000 };
        template.DynamicBonuses.Add(new DynamicBonusTemplate
        {
            Attribute = UnitAttribute.MeleeSpeedMul,
            ModifierType = UnitModifierType.Value,
            FuncId = 1,
            FuncType = DynamicBonusFuncRules.LinearFuncType
        });

        // Just applied: ratio ~0, so the row carries StartValue. Both reads interpolate from the wall
        // clock, so they agree only within the microseconds between them.
        StartBuff(template, owner, index: 9u, startedAt: DateTime.UtcNow);
        var bonus = owner.GetDynamicBonuses(UnitAttribute.MeleeSpeedMul).Single();
        await Assert.That(bonus.Evaluate(out var atStart)).IsTrue();
        await Assert.That(Math.Abs(atStart - (-250d)) < 50d).IsTrue();
        await Assert.That(Math.Abs(owner.CalculateWithBonuses(0d, UnitAttribute.MeleeSpeedMul) - (-250d)) < 50d).IsTrue();

        // Past its duration: ratio clamps to 1, so the row carries EndValue.
        bonus.SourceBuff.StartTime = DateTime.UtcNow.AddSeconds(-30);
        await Assert.That(bonus.Evaluate(out var atEnd)).IsTrue();
        await Assert.That(atEnd).IsEqualTo(-600d);
        await Assert.That(owner.CalculateWithBonuses(0d, UnitAttribute.MeleeSpeedMul)).IsEqualTo(-600d);
    }

    [Test]
    public async Task Evaluate_FormulaFuncContributesNothingWhileAnotherFormulaIsRunning()
    {
        // A row that reads the attribute it modifies comes back through CalculateWithBonuses; the
        // nested evaluation has to yield nothing or a self-referencing row would never terminate.
        using var formulas = InstallFormulaFunc(14, "attr_6");
        var owner = new Unit { MaxHp = 500 };
        var template = new BuffTemplate { Id = 28981 };
        template.DynamicBonuses.Add(new DynamicBonusTemplate
        {
            Attribute = UnitAttribute.MaxHealth,
            ModifierType = UnitModifierType.Value,
            FuncId = 14,
            FuncType = DynamicBonusFuncRules.FormulaFuncType
        });
        StartBuff(template, owner, index: 10u);
        var bonus = owner.GetDynamicBonuses(UnitAttribute.MaxHealth).Single();

        bool nested;
        using (FormulaFuncRules.BeginFormulaEvaluation())
            nested = bonus.Evaluate(out _);

        await Assert.That(nested).IsFalse();
        await Assert.That(FormulaFuncRules.InFormulaEvaluation).IsFalse();
        await Assert.That(bonus.Evaluate(out var value)).IsTrue();
        await Assert.That(value).IsEqualTo(500d);
    }

    [Test]
    public async Task Start_UnsupportedFuncType_RegistersNothing()
    {
        // dynamic_unit_modifiers 587: buff 23244, attribute 6, func_type DynamicFunc, func_id 27. Its
        // func_id resolves in the formulas table (there it is "2440 + 100 * pc_level^( ( pc_level - 49
        // ) / 6 )"), whose variables the damage/skill pipelines own; nothing is applied and the row is
        // reported once at load instead.
        var owner = new Unit { MaxHp = 500 };
        var template = new BuffTemplate { Id = 23244 };
        template.DynamicBonuses.Add(new DynamicBonusTemplate
        {
            Attribute = UnitAttribute.MaxHealth,
            ModifierType = UnitModifierType.Value,
            FuncId = 27,
            FuncType = "DynamicFunc"
        });

        StartBuff(template, owner, index: 11u);

        await Assert.That(owner.GetDynamicBonuses(UnitAttribute.MaxHealth)).IsEmpty();
        await Assert.That(owner.CalculateWithBonuses(0d, UnitAttribute.MaxHealth)).IsEqualTo(0d);
    }

    [Test]
    public async Task Start_FormulaFuncWithAMissingRow_RegistersNothing()
    {
        // A dynamic row whose formula_funcs row is absent (or was dropped at load) must leave the
        // attribute alone rather than applying a guessed value.
        var owner = new Unit { MaxHp = 500 };
        var template = new BuffTemplate { Id = 28981 };
        template.DynamicBonuses.Add(new DynamicBonusTemplate
        {
            Attribute = UnitAttribute.MaxHealth,
            ModifierType = UnitModifierType.Value,
            FuncId = 4242,
            FuncType = DynamicBonusFuncRules.FormulaFuncType
        });

        StartBuff(template, owner, index: 12u);

        await Assert.That(owner.GetDynamicBonuses(UnitAttribute.MaxHealth)).IsEmpty();
    }

    private static SingletonScope<FormulaManager> InstallFormulaFunc(uint id, string text)
    {
        var formulas = new FormulaManager();
        formulas.RegisterFormulaFunc(id, text);
        return new SingletonScope<FormulaManager>(formulas);
    }

    private static Buff StartBuff(BuffTemplate template, Unit owner, uint index, DateTime? startedAt = null)
    {
        var buff = new Buff(owner, owner, new SkillCasterUnit(1), template, null, startedAt ?? DateTime.UtcNow)
        {
            Index = index,
            Passive = true, // skips the SCBuffCreatedPacket broadcast inside Start()
            AbLevel = 1
        };
        buff.Duration = template.Duration;
        template.Start(owner, owner, buff);
        return buff;
    }

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private sealed class SingletonScope<T> : IDisposable where T : class
    {
        private readonly FieldInfo _field = typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly object _previous;

        public SingletonScope(T value)
        {
            _previous = _field.GetValue(null);
            _field.SetValue(null, value);
        }

        public void Dispose() => _field.SetValue(null, _previous);
    }
}
