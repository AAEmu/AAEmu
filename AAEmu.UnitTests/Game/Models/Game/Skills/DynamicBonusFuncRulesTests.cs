using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// Which <c>dynamic_unit_modifiers.func_type</c> values the server evaluates, and the single report
/// line for the rest. The row counts and func_ids are the shipped 10.0.2.13 content.
/// </summary>
public class DynamicBonusFuncRulesTests
{
    [Test]
    public async Task IsSupported_AcceptsTheImplementedFuncTypes()
    {
        await Assert.That(DynamicBonusFuncRules.IsSupported("LinearFunc")).IsTrue();
        await Assert.That(DynamicBonusFuncRules.IsSupported("FormulaFunc")).IsTrue();
    }

    [Test]
    public async Task IsSupported_RejectsTheFuncTypesThatResolveInTheFormulasTable()
    {
        await Assert.That(DynamicBonusFuncRules.IsSupported("DynamicFunc")).IsFalse();
        await Assert.That(DynamicBonusFuncRules.IsSupported("ManualFunc")).IsFalse();
        await Assert.That(DynamicBonusFuncRules.IsSupported("")).IsFalse();
    }

    [Test]
    public async Task SummarizeUnsupported_NamesCountTypesAndFuncIdsInOneLine()
    {
        // The 12 DynamicFunc rows (func_ids 3,4,5,10,11,19,20,24,25,27,29,30 on buffs 17278, 11194,
        // 17929, 20963, 2927, 22712, 23237, 23244, 27159, 27162) and the 2 ManualFunc rows
        // (func_ids 11 and 15 on buffs 930 and 1639).
        uint[] dynamicFuncIds = [3, 4, 5, 10, 11, 19, 20, 24, 25, 27, 29, 30];
        var rows = dynamicFuncIds
            .Select(funcId => Row("DynamicFunc", funcId))
            .Concat([Row("ManualFunc", 11), Row("ManualFunc", 15)])
            .ToList();

        var summary = DynamicBonusFuncRules.SummarizeUnsupported(rows);

        await Assert.That(summary).Contains("14 dynamic_unit_modifiers rows");
        await Assert.That(summary).Contains("DynamicFunc: 12 func_ids (3,4,5,10,11,19,20,24,25,27,29,30)");
        await Assert.That(summary).Contains("ManualFunc: 2 func_ids (11,15)");
        await Assert.That(summary.Contains('\n')).IsFalse();
    }

    [Test]
    public async Task SummarizeUnsupported_IsSilentWhenEveryRowIsImplemented()
    {
        // The 466 LinearFunc and 233 FormulaFunc rows are the ones this server evaluates.
        var rows = new List<DynamicBonusTemplate> { Row("LinearFunc", 1), Row("LinearFunc", 2), Row("FormulaFunc", 17) };

        await Assert.That(DynamicBonusFuncRules.SummarizeUnsupported(rows)).IsNull();
        await Assert.That(DynamicBonusFuncRules.SummarizeUnsupported([])).IsNull();
    }

    [Test]
    public async Task SummarizeUnsupported_CountsRowsAndDistinctFuncIdsSeparately()
    {
        // Two rows sharing one func_id (buff 23237 and 27159 both use a set-item armour row shape)
        // are two dropped rows but one func_id.
        var rows = new List<DynamicBonusTemplate> { Row("DynamicFunc", 29), Row("DynamicFunc", 29), Row("LinearFunc", 5) };

        var summary = DynamicBonusFuncRules.SummarizeUnsupported(rows);

        await Assert.That(summary).Contains("2 dynamic_unit_modifiers rows");
        await Assert.That(summary).Contains("DynamicFunc: 1 func_ids (29)");
    }

    private static DynamicBonusTemplate Row(string funcType, uint funcId) =>
        new() { FuncType = funcType, FuncId = funcId, Attribute = UnitAttribute.MaxHealth };
}
