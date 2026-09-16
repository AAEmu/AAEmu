using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// Binding of a <c>formula_funcs</c> expression to the unit that owns the buff. Every expression
/// quoted here is a shipped 10.0.2.13 row, named by <c>formula_funcs.id</c> and by the
/// <c>dynamic_unit_modifiers</c> row that references it (content DB, read-only).
/// </summary>
public class FormulaFuncRulesTests
{
    [Test]
    public async Task ParseVariables_MapsAttrIdentifiersToUnitAttributeIds()
    {
        // formula_funcs 1, referenced by dynamic_unit_modifiers 721: buff 28927
        // "(attr_test_func) 수식 검사", unit_attribute_id 0 (str per enum_unit_attribute).
        var variables = FormulaFuncRules.ParseVariables("attr_0 + attr_1 + attr_2 + attr_3 + attr_4");

        await Assert.That(variables.Count).IsEqualTo(5);
        await Assert.That(variables[0])
            .IsEqualTo(new FormulaFuncVariable("attr_0", FormulaFuncVariableSource.Attribute, 0u));
        await Assert.That(variables[4])
            .IsEqualTo(new FormulaFuncVariable("attr_4", FormulaFuncVariableSource.Attribute, 4u));
    }

    [Test]
    public async Task ParseVariables_KeepsTheEngineFunctionsOutOfTheBinding()
    {
        // formula_funcs 235, referenced by dynamic_unit_modifiers 955 (buff 29238, attribute 6
        // max_health, percent). clamp/min/max/floor are registered on the Jace engine, not variables.
        var variables = FormulaFuncRules.ParseVariables("clamp ( -49/16000*gear_score + 62.25 , 1 , 50 )");

        await Assert.That(variables.Count).IsEqualTo(1);
        await Assert.That(variables[0].Name).IsEqualTo("gear_score");
        await Assert.That(variables[0].Source).IsEqualTo(FormulaFuncVariableSource.GearScore);
    }

    [Test]
    public async Task ParseVariables_ReadsPcLevelAndHeirLevel()
    {
        // formula_funcs 2 (dynamic_unit_modifiers 722, buff 28927, attribute 1 dex) and
        // formula_funcs 241 (dynamic_unit_modifiers 961, buff 29238, attribute 220
        // exp_by_kill_monster_mul).
        var both = FormulaFuncRules.ParseVariables("pc_level + heir_level");
        var pcOnly = FormulaFuncRules.ParseVariables("if_negative( pc_level - 55 , -90 , 0)");

        await Assert.That(both.Count).IsEqualTo(2);
        await Assert.That(both[0].Source).IsEqualTo(FormulaFuncVariableSource.PcLevel);
        await Assert.That(both[1].Source).IsEqualTo(FormulaFuncVariableSource.HeirLevel);
        await Assert.That(pcOnly.Count).IsEqualTo(1);
        await Assert.That(pcOnly[0].Name).IsEqualTo("pc_level");
    }

    [Test]
    public async Task ParseVariables_DeduplicatesRepeatedIdentifiers()
    {
        // formula_funcs 225 (dynamic_unit_modifiers 945, buff 29238, attribute 249
        // melee_dps_inc_anti_npc) reads attr_96 (mainhand_dps) twice.
        var variables = FormulaFuncRules.ParseVariables(
            "clamp ( 0.26 * (attr_96/1000) * 1000 ^ 1.1 + 1000000 - attr_96 , 5000 , 1000000 )");

        await Assert.That(variables.Count).IsEqualTo(1);
        await Assert.That(variables[0].Name).IsEqualTo("attr_96");
        await Assert.That(variables[0].AttributeId).IsEqualTo(96u);
    }

    [Test]
    public async Task BindVariables_TakesPcLevelAndHeirLevelFromTheUnit()
    {
        var unit = new Unit { Level = 30, HeirLevel = 4 };

        var named = FormulaFuncRules.BindVariables(
            FormulaFuncRules.ParseVariables("pc_level + heir_level"), unit);
        var gearScore = FormulaFuncRules.BindVariables(
            FormulaFuncRules.ParseVariables("gear_score"), unit);

        await Assert.That(named["pc_level"]).IsEqualTo(30d);
        await Assert.That(named["heir_level"]).IsEqualTo(4d);
        // Gear score belongs to a character; a unit without one binds 0.
        await Assert.That(gearScore["gear_score"]).IsEqualTo(0d);
    }

    [Test]
    public async Task ReadAttribute_ReadsThePropertyCarryingTheAttribute()
    {
        // Attribute 6 is max_health (enum_unit_attribute 6), carried by Unit.MaxHp.
        var unit = new Unit { MaxHp = 500 };

        await Assert.That(FormulaFuncRules.ReadAttribute(unit, 6)).IsEqualTo(500d);
    }

    [Test]
    public async Task ReadAttribute_ReadsThePropertyAtItsOwnScale()
    {
        // Attribute 10 is move_speed_mul, carried by Unit.MoveSpeedMul, which is the 1000-based
        // accumulator divided by 1000: a bare unit reads 1.0 where the raw modifier scale is 1000.
        var unit = new Unit();

        await Assert.That(FormulaFuncRules.ReadAttribute(unit, 10)).IsEqualTo(1d);

        unit.AddBonus(1u, new Bonus
        {
            Template = new BonusTemplate { Attribute = UnitAttribute.MoveSpeedMul, Value = 500 },
            Value = 500
        });

        await Assert.That(FormulaFuncRules.ReadAttribute(unit, 10)).IsEqualTo(1.5d);
    }

    [Test]
    public async Task ReadAttribute_FallsBackToTheModifierAccumulatorForIdsNoClassCarries()
    {
        // Attribute 0 (str) is not a property of the base Unit, but its modifier rows still collect.
        var unit = new Unit();
        unit.AddBonus(1u, new Bonus
        {
            Template = new BonusTemplate { Attribute = UnitAttribute.Str, Value = 7 },
            Value = 7
        });

        await Assert.That(FormulaFuncRules.ReadAttribute(unit, 0)).IsEqualTo(7d);
    }

    [Test]
    public async Task RegisterFormulaFunc_DropsRowsTheEngineCannotResolve()
    {
        // formula_funcs 17 is "attr_10" (dynamic_unit_modifiers 737, buff 28984
        // "(attr_test_func) 10 move_speed_mul"). The other two are the shapes a bad row takes: an
        // identifier no binder knows and a function the engine does not register. Both are dropped
        // once at load, so no attribute read can reach them.
        var formulas = new FormulaManager();

        var good = formulas.RegisterFormulaFunc(17, "attr_10");
        var unknownVariable = formulas.RegisterFormulaFunc(900, "attr_10 + not_a_variable");
        var unknownFunction = formulas.RegisterFormulaFunc(901, "attr_10 + not_a_function(2)");

        await Assert.That(good).IsNotNull();
        await Assert.That(good.Variables.Count).IsEqualTo(1);
        await Assert.That(unknownVariable).IsNull();
        await Assert.That(unknownFunction).IsNull();
        await Assert.That(formulas.GetFormulaFunc(17)).IsNotNull();
        await Assert.That(formulas.GetFormulaFunc(900)).IsNull();
        await Assert.That(formulas.GetFormulaFunc(901)).IsNull();
        await Assert.That(formulas.RejectedFormulaFuncs.Count).IsEqualTo(2);
        await Assert.That(formulas.RejectedFormulaFuncs.Contains(900u)).IsTrue();
        await Assert.That(formulas.RejectedFormulaFuncs.Contains(901u)).IsTrue();
    }

    [Test]
    public async Task FormulaFuncTemplate_ReportsFailureInsteadOfThrowingOnAnUnresolvableRow()
    {
        // A row that reached a live bonus anyway (registered by hand, or compiled by a future client)
        // must not take the world down when the attribute is read: it yields no value.
        var template = new FormulaFuncTemplate
        {
            Id = 902,
            Formula = new Formula("attr_10 + not_a_variable"),
            Variables = FormulaFuncRules.ParseVariables("attr_10 + not_a_variable")
        };

        var evaluated = template.TryEvaluate(new Unit(), out var value);

        await Assert.That(evaluated).IsFalse();
        await Assert.That(value).IsEqualTo(0d);
    }

    [Test]
    public async Task TryEvaluate_ReturnsTheShippedGearScoreRowForARealGearScore()
    {
        // formula_funcs 235: "clamp ( -49/16000*gear_score + 62.25 , 1 , 50 )". At the clamp's nominal
        // gear score of 8000: -49/16000*8000 + 62.25 = 37.75, inside 1..50.
        var formulas = new FormulaManager();
        var row = formulas.RegisterFormulaFunc(235, "clamp ( -49/16000*gear_score + 62.25 , 1 , 50 )");
        var parameters = FormulaFuncRules.BindVariables(row.Variables, new Unit());
        parameters["gear_score"] = 8000d; // a character's score; a bare Unit has none

        var evaluated = row.Formula.TryEvaluate(parameters, out var value);

        await Assert.That(evaluated).IsTrue();
        await Assert.That(Math.Abs(value - 37.75d) < 1e-9).IsTrue();
    }
}
