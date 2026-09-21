using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Plots;
using AAEmu.Game.Models.Game.Skills.Plots.Tree;
using AAEmu.Game.Models.Game.Skills.Plots.Type;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.NPChar;

public class NpcInteractionCastRulesTests
{
    private const uint OriginalSource = (uint)PlotSourceUpdateMethodType.OriginalSource;
    private const uint OriginalTarget = (uint)PlotTargetUpdateMethodType.OriginalTarget;
    private const uint PreviousSource = (uint)PlotSourceUpdateMethodType.PreviousSource;
    private const uint PreviousTarget = (uint)PlotTargetUpdateMethodType.PreviousTarget;

    [Test]
    public async Task UseTargetNpcAsCaster_RequiresTheSkillToBeOnTheAuthoredSet()
    {
        await Assert.That(NpcInteractionCastRules.UseTargetNpcAsCaster(
            false, OriginalSource, OriginalSource)).IsFalse();
    }

    [Test]
    public async Task UseTargetNpcAsCaster_RequiresTheRootEventToBeAnOriginalSourceSelfCast()
    {
        await Assert.That(NpcInteractionCastRules.UseTargetNpcAsCaster(
            true, OriginalSource, OriginalSource)).IsTrue();
        await Assert.That(NpcInteractionCastRules.UseTargetNpcAsCaster(
            true, PreviousSource, PreviousTarget)).IsFalse();
        await Assert.That(NpcInteractionCastRules.UseTargetNpcAsCaster(
            true, OriginalSource, OriginalTarget)).IsFalse();
        await Assert.That(NpcInteractionCastRules.UseTargetNpcAsCaster(
            true, OriginalTarget, OriginalSource)).IsFalse();
    }

    [Test]
    public async Task TryResolveNpcCaster_UsesTheTargetedNpcWhenThePlotIsAnOriginalSourceSelfCast()
    {
        var player = new Character(new UnitCustomModelParams()) { ObjId = 10 };
        var npc = new Npc { ObjId = 20 };
        var template = TemplateWithRoot(OriginalSource, OriginalSource);

        var resolved = NpcInteractionCastRules.TryResolveNpcCaster(
            player, npc, 7, template, [7, 8], out var caster);

        await Assert.That(resolved).IsTrue();
        await Assert.That(caster).IsEqualTo(npc);
    }

    [Test]
    public async Task TryResolveNpcCaster_LeavesPlayerCastsAloneWhenThePlotStartsFromPreviousUnits()
    {
        var player = new Character(new UnitCustomModelParams()) { ObjId = 10 };
        var npc = new Npc { ObjId = 20 };
        var template = TemplateWithRoot(PreviousSource, PreviousTarget);

        var resolved = NpcInteractionCastRules.TryResolveNpcCaster(
            player, npc, 7, template, [7], out var caster);

        await Assert.That(resolved).IsFalse();
        await Assert.That(caster).IsNull();
    }

    [Test]
    public async Task TryResolveNpcCaster_RefusesASkillTheNpcDoesNotOffer()
    {
        var player = new Character(new UnitCustomModelParams()) { ObjId = 10 };
        var npc = new Npc { ObjId = 20 };
        var template = TemplateWithRoot(OriginalSource, OriginalSource);

        var resolved = NpcInteractionCastRules.TryResolveNpcCaster(
            player, npc, 9, template, [7, 8], out var caster);

        await Assert.That(resolved).IsFalse();
        await Assert.That(caster).IsNull();
    }

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

    private static SkillTemplate TemplateWithRoot(uint sourceUpdate, uint targetUpdate) =>
        new()
        {
            Plot = new Plot
            {
                Tree = new PlotTree(1)
                {
                    RootNode = new PlotNode
                    {
                        Event = new PlotEventTemplate
                        {
                            SourceUpdateMethodId = sourceUpdate,
                            TargetUpdateMethodId = targetUpdate
                        }
                    }
                }
            }
        };
}
