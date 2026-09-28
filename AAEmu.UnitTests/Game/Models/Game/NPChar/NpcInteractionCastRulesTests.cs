using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Plots;
using AAEmu.Game.Models.Game.Skills.Plots.Tree;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.NPChar;

[NotInParallel]
public class NpcInteractionCastRulesTests
{
    [Test]
    public async Task PlotTimelineMatches_UsesTheLiveSkillIdAndOnlyFallsBackAfterItIsReleased()
    {
        var skill = new Skill { TlId = 208 };
        var state = new PlotState(new Unit { ObjId = 1 }, new SkillCasterUnit(1),
            new Unit { ObjId = 1 }, new SkillCastUnitTarget(1), new SkillObject(), skill, 208);

        await Assert.That(NpcInteractionCastRules.PlotTimelineMatches(state, 208)).IsTrue();
        await Assert.That(NpcInteractionCastRules.PlotTimelineMatches(state, 209)).IsFalse();

        skill.TlId = 0;
        await Assert.That(NpcInteractionCastRules.PlotTimelineMatches(state, 208)).IsTrue();
        await Assert.That(NpcInteractionCastRules.PlotTimelineMatches(state, 209)).IsFalse();
    }

    [Test]
    public async Task PlotTimelineMatches_DoesNotTreatARecycledLaunchIdAsTheLivePlot()
    {
        var skill = new Skill { TlId = 300 };
        var state = new PlotState(new Unit { ObjId = 1 }, new SkillCasterUnit(1),
            new Unit { ObjId = 1 }, new SkillCastUnitTarget(1), new SkillObject(), skill, 208);

        await Assert.That(NpcInteractionCastRules.PlotTimelineMatches(state, 208)).IsFalse();
        await Assert.That(NpcInteractionCastRules.PlotTimelineMatches(state, 300)).IsTrue();
    }

    [Test]
    public async Task FindPlotState_ReadsTheInteractionNpcWhenThePlayerBarIsEmpty()
    {
        var player = new Character(new UnitCustomModelParams()) { ObjId = 10 };
        var npc = new Npc { ObjId = 20 };
        var skill = new Skill { TlId = 0 };
        var state = new PlotState(npc, new SkillCasterUnit(npc.ObjId), npc,
            new SkillCastUnitTarget(npc.ObjId), new SkillObject(), skill, 262);
        npc.ActivePlotState = state;
        player.CurrentInteractionObject = npc;

        var found = NpcInteractionCastRules.FindPlotState(player, 262);

        await Assert.That(found).IsEqualTo(state);
        await Assert.That(NpcInteractionCastRules.FindPlotState(player, 1)).IsNull();
    }

    [Test]
    [Arguments(11u, 11u, true)]
    [Arguments(11u, 12u, false)]
    [Arguments(11u, 0u, false)]
    public async Task IsOfferedInteractionSkill_AnswersFromTheNpcsOwnSet(uint offered, uint asked, bool expected)
    {
        NpcInteractionGameData.Instance.SetForTest(new Dictionary<uint, uint[]> { [161] = [11] });
        try
        {
            var npc = new Npc { ObjId = 20, Template = new NpcTemplate { NpcInteractionSetId = 161 } };
            var otherSet = new Npc { ObjId = 21, Template = new NpcTemplate { NpcInteractionSetId = 162 } };
            var noSet = new Npc { ObjId = 22, Template = new NpcTemplate() };

            await Assert.That(NpcInteractionCastRules.IsOfferedInteractionSkill(npc, asked)).IsEqualTo(expected);
            // A set that carries no rows, one the template does not name, and an NPC with no template at all
            // all answer false rather than guessing.
            await Assert.That(NpcInteractionCastRules.IsOfferedInteractionSkill(otherSet, offered)).IsFalse();
            await Assert.That(NpcInteractionCastRules.IsOfferedInteractionSkill(noSet, offered)).IsFalse();
            await Assert.That(NpcInteractionCastRules.IsOfferedInteractionSkill(new Npc { ObjId = 23 }, offered)).IsFalse();
            await Assert.That(NpcInteractionCastRules.IsOfferedInteractionSkill(null, offered)).IsFalse();
        }
        finally
        {
            NpcInteractionGameData.Instance.SetForTest(null);
        }
    }
}
