using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Plots;
using AAEmu.Game.Models.Game.Skills.Plots.Tree;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.NPChar;

/// <summary>
/// Authored interaction skills stay player-cast. The client already names the player as caster and
/// the NPC as target, and the plot's own source selection is what finds that NPC. Swapping the
/// caster onto the NPC would send the plot's gates and effects to the NPC instead.
/// </summary>
public static class NpcInteractionCastRules
{
    /// <summary>
    /// The plot the client is asking to stop: the player's own bar, or an interaction NPC that is
    /// already running a plot on the player's timeline.
    /// </summary>
    public static PlotState FindPlotState(Character character, ushort plotTlId)
    {
        if (character == null || plotTlId == 0)
            return null;

        if (PlotTimelineMatches(character.ActivePlotState, plotTlId))
            return character.ActivePlotState;

        if (character.CurrentInteractionObject is Unit interacted &&
            PlotTimelineMatches(interacted.ActivePlotState, plotTlId))
            return interacted.ActivePlotState;

        return null;
    }

    /// <summary>
    /// The id the client quotes is the one the plot was launched with. A cast-time skill clears its
    /// own <c>TlId</c> when the cast ends, so a still-running graph is matched on
    /// <see cref="PlotState.CastTlId"/> only after that release. A live skill id always wins, so a
    /// recycled launch id cannot cancel a newer plot that already owns a different timeline.
    /// </summary>
    public static bool PlotTimelineMatches(PlotState state, ushort plotTlId)
    {
        if (state?.ActiveSkill == null || plotTlId == 0)
            return false;
        if (state.ActiveSkill.TlId == plotTlId)
            return true;
        return state.ActiveSkill.TlId == 0 && state.CastTlId == plotTlId;
    }
}
