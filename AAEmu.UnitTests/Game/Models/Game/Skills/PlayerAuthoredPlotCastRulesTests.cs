using AAEmu.Game.Models.Game.Skills.Plots;
using AAEmu.Game.Models.Game.Skills.Plots.Tree;
using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// Which casts hand the end of the cast to their plot graph. A cast with no casting time is the whole of
/// the condition: it is the only cast shape that never receives the <c>SCSkillStarted</c> that acknowledges
/// the key press, so its graph's packets all name a timeline the client has no live cast on.
/// </summary>
public class PlayerAuthoredPlotCastRulesTests
{
    private const uint OfferedSkillId = 1;

    private static SkillTemplate Template(bool plot = true, bool plotOnly = false, int castingTime = 0) =>
        new()
        {
            Id = OfferedSkillId,
            CastingTime = castingTime,
            PlotOnly = plotOnly,
            Plot = plot ? new Plot { Id = 7 } : null
        };

    [Test]
    public async Task OfferedInstantPlotAction_HandsTheEndToItsGraph()
    {
        await Assert.That(PlayerAuthoredPlotCastRules.PlotOwnsCastEnd(
                isPlayerCast: true, isOfferedInteractionAction: true,
                template: Template(), forcePlotGraphOnly: false))
            .IsTrue();
    }

    [Test]
    public async Task NotOfferedByAnySet_KeepsTheCastEndOnTheCast()
    {
        // The ability kit is never reached through an interaction bar, so its instant plot casts keep the
        // end they have always had.
        await Assert.That(PlayerAuthoredPlotCastRules.PlotOwnsCastEnd(
                isPlayerCast: true, isOfferedInteractionAction: false,
                template: Template(), forcePlotGraphOnly: false))
            .IsFalse();
    }

    [Test]
    public async Task NotAPlayerCast_KeepsTheCastEndOnTheCast()
    {
        // An NPC running the same template already owns its own end through its own script path.
        await Assert.That(PlayerAuthoredPlotCastRules.PlotOwnsCastEnd(
                isPlayerCast: false, isOfferedInteractionAction: true,
                template: Template(), forcePlotGraphOnly: false))
            .IsFalse();
    }

    [Test]
    public async Task WithoutAPlot_ThereIsNoGraphToOwnTheEnd()
    {
        await Assert.That(PlayerAuthoredPlotCastRules.PlotOwnsCastEnd(
                isPlayerCast: true, isOfferedInteractionAction: true,
                template: Template(plot: false), forcePlotGraphOnly: false))
            .IsFalse();
    }

    [Test]
    public async Task WithoutATemplate_ThereIsNoGraphToOwnTheEnd()
    {
        await Assert.That(PlayerAuthoredPlotCastRules.PlotOwnsCastEnd(
                isPlayerCast: true, isOfferedInteractionAction: true,
                template: null, forcePlotGraphOnly: false))
            .IsFalse();
    }

    [Test]
    public async Task PlotOnly_AlreadyOwnsItsOwnEndAndIsLeftAlone()
    {
        await Assert.That(PlayerAuthoredPlotCastRules.PlotOwnsCastEnd(
                isPlayerCast: true, isOfferedInteractionAction: true,
                template: Template(plotOnly: true), forcePlotGraphOnly: false))
            .IsFalse();
    }

    [Test]
    public async Task ForcePlotGraphOnly_AlreadyOwnsItsOwnEndAndIsLeftAlone()
    {
        await Assert.That(PlayerAuthoredPlotCastRules.PlotOwnsCastEnd(
                isPlayerCast: true, isOfferedInteractionAction: true,
                template: Template(), forcePlotGraphOnly: true))
            .IsFalse();
    }

    [Test]
    public async Task CastTime_AlreadySendsItsOwnStartedAndIsLeftAlone()
    {
        await Assert.That(PlayerAuthoredPlotCastRules.PlotOwnsCastEnd(
                isPlayerCast: true, isOfferedInteractionAction: true,
                template: Template(castingTime: 1500), forcePlotGraphOnly: false))
            .IsFalse();
    }
}
