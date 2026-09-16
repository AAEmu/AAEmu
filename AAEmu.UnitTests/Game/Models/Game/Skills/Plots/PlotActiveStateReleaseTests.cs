using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Plots;
using AAEmu.Game.Models.Game.Skills.Plots.Tree;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Plots;

/// <summary>
/// Overlapping plots end on different threads: a combo or a plot_only follow-up cancels the plot that is
/// already on the bar (Plot.RunAsync), and that tree then runs its own end path while the newer plot owns
/// both <c>ActivePlotState</c> slots. The end path may only clear the state it owns.
/// </summary>
public class PlotActiveStateReleaseTests
{
    private static PlotState NewState(Unit caster, Skill skill) =>
        new(caster, new SkillCasterUnit(caster.ObjId), caster, new SkillCastUnitTarget(caster.ObjId),
            new SkillObject(), skill);

    [Test]
    public async Task Release_ClearsOnlyItsOwnState()
    {
        var unit = new Unit { ObjId = 1 };
        var skill = new Skill { Id = 100 };
        var mine = NewState(unit, skill);
        var other = NewState(unit, skill);
        unit.ActivePlotState = mine;
        skill.ActivePlotState = mine;

        await Assert.That(unit.ReleaseActivePlotState(other)).IsFalse();
        await Assert.That(skill.ReleaseActivePlotState(other)).IsFalse();
        await Assert.That(unit.ActivePlotState).IsSameReferenceAs(mine);
        await Assert.That(skill.ActivePlotState).IsSameReferenceAs(mine);

        await Assert.That(unit.ReleaseActivePlotState(mine)).IsTrue();
        await Assert.That(skill.ReleaseActivePlotState(mine)).IsTrue();
        await Assert.That(unit.ActivePlotState).IsNull();
        await Assert.That(skill.ActivePlotState).IsNull();
    }

    [Test]
    public async Task Release_OfNothing_IsANoOp()
    {
        var unit = new Unit { ObjId = 2 };
        var skill = new Skill { Id = 101 };

        await Assert.That(unit.ReleaseActivePlotState(null)).IsFalse();
        await Assert.That(skill.ReleaseActivePlotState(null)).IsFalse();
    }

    [Test]
    public async Task ConcurrentRelease_ExactlyOneCallWins()
    {
        var unit = new Unit { ObjId = 3 };
        var skill = new Skill { Id = 102 };
        var state = NewState(unit, skill);
        unit.ActivePlotState = state;
        skill.ActivePlotState = state;

        var unitWins = 0;
        var skillWins = 0;
        Parallel.For(0, 32, _ =>
        {
            // Reads and writes race with these calls exactly as two plot threads do.
            if (unit.ReleaseActivePlotState(state))
                Interlocked.Increment(ref unitWins);
            if (skill.ReleaseActivePlotState(state))
                Interlocked.Increment(ref skillWins);
        });

        await Assert.That(unitWins).IsEqualTo(1);
        await Assert.That(skillWins).IsEqualTo(1);
        await Assert.That(unit.ActivePlotState).IsNull();
    }

    [Test]
    public async Task DropPlotState_LeavesANewerStateInPlace()
    {
        var caster = new Unit { ObjId = 4 };
        var oldSkill = new Skill { Id = 103, Template = new SkillTemplate { Id = 103 } };
        var newerSkill = new Skill { Id = 104, Template = new SkillTemplate { Id = 104 } };
        var oldState = NewState(caster, oldSkill);
        var newerState = NewState(caster, newerSkill);

        caster.ActivePlotState = oldState;
        oldSkill.ActivePlotState = oldState;
        // The newer cast claims the slots before the older tree gets to its end path.
        caster.ActivePlotState = newerState;
        newerSkill.ActivePlotState = newerState;

        PlotTree.DropPlotState(oldState);

        await Assert.That(caster.ActivePlotState).IsSameReferenceAs(newerState);
        await Assert.That(newerSkill.ActivePlotState).IsSameReferenceAs(newerState);
        await Assert.That(oldSkill.ActivePlotState).IsNull();
    }
}
