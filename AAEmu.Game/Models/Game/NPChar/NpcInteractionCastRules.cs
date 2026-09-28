using AAEmu.Game.GameData;
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
    /// Whether <paramref name="skillId"/> is one of the actions the NPC's authored interaction set offers.
    /// </summary>
    /// <remarks>
    /// The answer comes from the set the template names (<c>npcs.npc_interaction_set_id</c>) through the
    /// interaction tables, never from the skill's name or from a list written into the code. An NPC with no
    /// set, an unknown set, or a set that does not carry this skill answers false, so a skill that only looks
    /// like an interaction action is never treated as one.
    /// </remarks>
    public static bool IsOfferedInteractionSkill(Npc interactionNpc, uint skillId)
    {
        if (interactionNpc?.Template == null || skillId == 0)
            return false;

        var setId = interactionNpc.Template.NpcInteractionSetId;
        foreach (var offered in NpcInteractionGameData.Instance.GetSkills(setId))
            if (offered == skillId)
                return true;

        return false;
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
