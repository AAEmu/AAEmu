using AAEmu.Game.Models.Game.Skills.Plots;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Plots;

public class PlotConditionRulesTests
{
    [Test]
    public async Task EveryDatabaseConditionKind_HasAnArm()
    {
        // The 20 rows of 10.0.2.13 enum_plot_condition_kinds. A kind that reaches PlotCondition's default
        // case answers true and logs "not implemented" once, so an id missing here is a gate that silently
        // does nothing. Read the names back off the DB with:
        //   select id, name from enum_plot_condition_kinds order by id;
        (int Id, string Name, PlotConditionHandling Handling)[] db =
        [
            (1, "level", PlotConditionHandling.Implemented),
            (2, "relation", PlotConditionHandling.Implemented),
            (3, "direction", PlotConditionHandling.Implemented),
            (5, "buff", PlotConditionHandling.Implemented),
            (6, "weapon_equip_status", PlotConditionHandling.Implemented),
            (7, "chance", PlotConditionHandling.Implemented),
            (8, "dead", PlotConditionHandling.Implemented),
            (9, "combat_dice_result", PlotConditionHandling.Implemented),
            (10, "instrument_type", PlotConditionHandling.Implemented),
            (11, "range", PlotConditionHandling.Implemented),
            (12, "variable", PlotConditionHandling.Implemented),
            (13, "unit_attribute", PlotConditionHandling.Implemented),
            (14, "actability", PlotConditionHandling.Implemented),
            (15, "stealth", PlotConditionHandling.Implemented),
            (16, "visible", PlotConditionHandling.Implemented),
            (17, "ab_level", PlotConditionHandling.Implemented),
            (18, "casting_useable", PlotConditionHandling.Implemented),
            (19, "combat_resource", PlotConditionHandling.Implemented),
            (20, "unit_reqs", PlotConditionHandling.Implemented),
            (21, "accrue_damage_monster", PlotConditionHandling.Permissive)
        ];

        await Assert.That(db.Length).IsEqualTo(20);
        await Assert.That(PlotConditionRules.DatabaseKinds.Count).IsEqualTo(db.Length);

        foreach (var (id, name, handling) in db)
        {
            var entry = PlotConditionRules.DatabaseKinds.Single(candidate => candidate.Id == id);
            await Assert.That(entry.DbName).IsEqualTo(name);
            await Assert.That(entry.Handling).IsEqualTo(handling);
            await Assert.That(PlotConditionRules.HandlingOf((PlotConditionType)id)).IsNotEqualTo(PlotConditionHandling.Unhandled);
        }
    }

    [Test]
    public async Task ConditionTypeEnum_CoversEveryDatabaseKind()
    {
        // The server enum has to name every kind the data uses, or the loader casts an id to a value the
        // switch cannot match and the condition falls through to the default arm.
        var named = Enum.GetValues<PlotConditionType>().Select(kind => (int)kind).ToHashSet();

        foreach (var (id, name, _) in PlotConditionRules.DatabaseKinds)
            await Assert.That(named.Contains(id)).IsTrue();
    }

    [Test]
    public async Task UnknownKind_IsReportedUnhandled()
    {
        await Assert.That(PlotConditionRules.HandlingOf((PlotConditionType)4)).IsEqualTo(PlotConditionHandling.Unhandled);
        await Assert.That(PlotConditionRules.HandlingOf((PlotConditionType)99)).IsEqualTo(PlotConditionHandling.Unhandled);
    }

    [Test]
    public async Task Relation_Others_ExcludesTheCasterItself()
    {
        // enum_skill_target_relation 5 "others": 41 conditions that used to answer true unconditionally.
        var caster = new Unit { ObjId = 10 };
        var other = new Unit { ObjId = 11 };

        await Assert.That(PlotConditionRules.RelationMatches(5, caster, other)).IsTrue();
        await Assert.That(PlotConditionRules.RelationMatches(5, caster, caster)).IsFalse();
    }

    [Test]
    public async Task Relation_RaidAndAny_FollowTargetSelection()
    {
        var caster = new Unit { ObjId = 20 };
        var other = new Unit { ObjId = 21 };

        // Relation ids resolve through SkillTargetingUtil.IsRelationValid, the same call the area search
        // uses to pick targets, so a condition can no longer disagree with the selection that fed it.
        await Assert.That(PlotConditionRules.RelationMatches(0, caster, other)).IsTrue();
        // Raid (12 conditions): the caster is always in its own raid. The stranger half of that check goes
        // through TeamManager, which the unit-test host cannot construct.
        await Assert.That(PlotConditionRules.RelationMatches(3, caster, caster)).IsTrue();
    }

    [Test]
    public async Task BuffStackRange_ReadsZeroAsUnbounded()
    {
        // (1,1) (10,10) (26,999) (1,4) (5,9) (10,20) are the shapes the 384 ranged rows use.
        await Assert.That(PlotConditionRules.BuffStackInRange(1, 1, 1)).IsTrue();
        await Assert.That(PlotConditionRules.BuffStackInRange(2, 1, 1)).IsFalse();
        await Assert.That(PlotConditionRules.BuffStackInRange(3, 26, 999)).IsFalse();
        await Assert.That(PlotConditionRules.BuffStackInRange(26, 26, 999)).IsTrue();
        await Assert.That(PlotConditionRules.BuffStackInRange(7, 5, 9)).IsTrue();
        await Assert.That(PlotConditionRules.BuffStackInRange(10, 5, 9)).IsFalse();
        // param4 = 0 on 7 rows: no upper bound, not "exactly zero stacks".
        await Assert.That(PlotConditionRules.BuffStackInRange(400, 1, 0)).IsTrue();
        await Assert.That(PlotConditionRules.BuffStackInRange(0, 1, 0)).IsFalse();
    }

    [Test]
    public async Task BuffStackRange_WithNoRange_AcceptsEveryStackCount()
    {
        // The other 9,728 kind-5 rows leave both params at 0 and must keep the old "tag is up" answer.
        for (var stacks = 0; stacks <= 5; stacks++)
            await Assert.That(PlotConditionRules.BuffStackInRange(stacks, 0, 0)).IsTrue();
    }

    [Test]
    public async Task CastBands_PartitionTheBarAndLeaveUnknownAlone()
    {
        // Plot 2557's ladder: exactly one band matches at every point of the bar.
        (int Min, int Max)[] bands = [(100, 100), (75, 99), (50, 74), (25, 49), (0, 24)];
        for (var progress = 0; progress <= 100; progress++)
        {
            var matches = bands.Count(band => PlotConditionRules.CastBandMatches(progress, band.Min, band.Max));
            await Assert.That(matches).IsEqualTo(1);
        }

        await Assert.That(PlotConditionRules.CastBandMatches(100, 100, 100)).IsTrue();
        await Assert.That(PlotConditionRules.CastBandMatches(99, 100, 100)).IsFalse();
        // No bar was ever advertised: the condition answers what it answered before it was implemented.
        await Assert.That(PlotConditionRules.CastBandMatches(null, 100, 100)).IsTrue();
    }

    [Test]
    public async Task CastProgress_SaturatesAtBothEnds()
    {
        var start = new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

        await Assert.That(PlotConditionRules.CastProgressPercent(start, 4000, start)).IsEqualTo(0);
        await Assert.That(PlotConditionRules.CastProgressPercent(start, 4000, start.AddMilliseconds(1000))).IsEqualTo(25);
        await Assert.That(PlotConditionRules.CastProgressPercent(start, 4000, start.AddMilliseconds(3999))).IsEqualTo(99);
        await Assert.That(PlotConditionRules.CastProgressPercent(start, 4000, start.AddMilliseconds(4000))).IsEqualTo(100);
        await Assert.That(PlotConditionRules.CastProgressPercent(start, 4000, start.AddSeconds(60))).IsEqualTo(100);
        await Assert.That(PlotConditionRules.CastProgressPercent(start, 0, start.AddSeconds(1))).IsEqualTo(100);
    }
}
