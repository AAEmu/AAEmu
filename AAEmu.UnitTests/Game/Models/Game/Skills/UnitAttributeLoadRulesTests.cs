using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class UnitAttributeLoadRulesTests
{
    [Test]
    public async Task IdsThatHaveNoMember_AreReportedAscendingAndOnceEach()
    {
        // 14 is used by two shipped Buff rows and enum_unit_attribute has no row for it; 44 and 10000
        // are not attributes at all. 21 stays a member (MeleeBlock) because unit_attribute_limits
        // names it, so it is not reported.
        var unknown = UnitAttributeLoadRules.UnknownIds([14, 14, 44, 10000, 21, 14]);

        await Assert.That(unknown).IsEquivalentTo(new List<uint> { 14, 44, 10000 });
        await Assert.That(unknown[0]).IsEqualTo(14u).Because("the report is ordered");
    }

    [Test]
    public async Task KnownIds_AreNotReported()
    {
        var unknown = UnitAttributeLoadRules.UnknownIds([0, 3, 64, 187, 256, 261, 289]);

        await Assert.That(unknown).IsEmpty();
    }

    [Test]
    public async Task NegativeIds_AreTheContentsNoAttributeMarker()
    {
        // Only actability_groups.unit_attr_id = -1 uses it, and it means "this lifeskill has no
        // attribute", which is not a gap in the enum.
        var unknown = UnitAttributeLoadRules.UnknownIds([-1, -1]);

        await Assert.That(unknown).IsEmpty();
    }

    [Test]
    public async Task NothingToReport_IsAnEmptyList()
    {
        await Assert.That(UnitAttributeLoadRules.UnknownIds([])).IsEmpty();
    }

    [Test]
    public async Task Warning_NamesTheSourceAndEveryId()
    {
        var warning = UnitAttributeLoadRules.Warning(
            "unit_modifiers (owner_type='Buff')",
            UnitAttributeLoadRules.UnknownIds([14]));

        await Assert.That(warning).Contains("unit_modifiers (owner_type='Buff')");
        await Assert.That(warning).Contains("14");
        // One line for the table, so the ids are listed and no row is named separately.
        await Assert.That(warning).DoesNotContain("\n");
    }

    [Test]
    public async Task Warning_ListsSeveralIdsOnTheSameLine()
    {
        var warning = UnitAttributeLoadRules.Warning(
            "unit_attribute_limits",
            UnitAttributeLoadRules.UnknownIds([44, 14, 14]));

        await Assert.That(warning).Contains("14, 44");
        await Assert.That(warning).DoesNotContain("\n");
    }
}
