using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// <c>skill_reagents.enable</c>. 10.0.2.13 ships 2 804 rows and two of them are disabled: row 3852
/// (skill 36492 -> item 43108) and row 4995 (skill 50997 -> item 54478). Both loaded anyway, so those
/// two skills charged a reagent the content had switched off.
/// </summary>
public class SkillReagentLoadRulesTests
{
    private static SkillReagent Row(uint id, uint skillId, uint itemId, int amount, bool enable = true) =>
        new() { Id = id, SkillId = skillId, ItemId = itemId, Amount = amount, Enable = enable };

    [Test]
    public async Task Enabled_KeepsTheEnabledRowsKeyedByRowId()
    {
        var rows = new List<SkillReagent>
        {
            Row(1, 100, 200, 1),
            Row(2, 100, 201, 2)
        };

        var loaded = SkillReagentLoadRules.Enabled(rows);

        await Assert.That(loaded.Count).IsEqualTo(2);
        await Assert.That(loaded[1].ItemId).IsEqualTo(200u);
        await Assert.That(loaded[2].Amount).IsEqualTo(2);
    }

    [Test]
    public async Task Enabled_DropsTheTwoShippedDisabledRows()
    {
        var rows = new List<SkillReagent>
        {
            Row(3852, 36492, 43108, 1, enable: false),
            Row(4995, 50997, 54478, 1, enable: false),
            Row(3853, 36492, 43109, 1)
        };

        var loaded = SkillReagentLoadRules.Enabled(rows);

        await Assert.That(loaded.Count).IsEqualTo(1);
        await Assert.That(loaded.ContainsKey(3852)).IsFalse();
        await Assert.That(loaded.ContainsKey(4995)).IsFalse();
        await Assert.That(loaded.ContainsKey(3853)).IsTrue();
    }

    [Test]
    public async Task DisabledRowIds_ListsTheSkippedRowsAscending()
    {
        var rows = new List<SkillReagent>
        {
            Row(4995, 50997, 54478, 1, enable: false),
            Row(10, 1, 2, 1),
            Row(3852, 36492, 43108, 1, enable: false)
        };

        var ids = SkillReagentLoadRules.DisabledRowIds(rows);

        await Assert.That(ids.Count).IsEqualTo(2);
        await Assert.That(ids[0]).IsEqualTo(3852u);
        await Assert.That(ids[1]).IsEqualTo(4995u);
    }

    [Test]
    public async Task DisabledWarning_IsOneLineNamingEachRowAndItsSkill()
    {
        var rows = new List<SkillReagent>
        {
            Row(3852, 36492, 43108, 1, enable: false),
            Row(4995, 50997, 54478, 1, enable: false),
            Row(1, 1, 1, 1)
        };

        var warning = SkillReagentLoadRules.DisabledWarning(rows);

        await Assert.That(warning).Contains("skill_reagents: 2 disabled row(s) skipped");
        await Assert.That(warning).Contains("row 3852 skips skill 36492");
        await Assert.That(warning).Contains("row 4995 skips skill 50997");
    }

    [Test]
    public async Task DisabledWarning_WithNothingDisabled_IsEmpty()
    {
        var rows = new List<SkillReagent> { Row(1, 1, 1, 1) };

        await Assert.That(SkillReagentLoadRules.DisabledWarning(rows)).IsEmpty();
    }

    [Test]
    public async Task Enabled_DefaultsToLoadingARowWithoutTheColumn()
    {
        // SkillReagent.Enable defaults to true, so a row built by a test or by an older snapshot is not
        // silently dropped.
        var row = new SkillReagent { Id = 7, SkillId = 8, ItemId = 9, Amount = 1 };

        await Assert.That(SkillReagentLoadRules.IsEnabled(row.Enable)).IsTrue();
        await Assert.That(SkillReagentLoadRules.Enabled([row]).Count).IsEqualTo(1);
    }
}
