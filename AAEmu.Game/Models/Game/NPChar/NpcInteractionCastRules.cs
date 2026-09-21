using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Plots;
using AAEmu.Game.Models.Game.Skills.Plots.Tree;
using AAEmu.Game.Models.Game.Skills.Plots.Type;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.NPChar;

/// <summary>
/// When a player picks an authored interaction skill, the client names the player as caster and the
/// NPC as target. Plots whose root event updates both source and target to
/// <see cref="PlotSourceUpdateMethodType.OriginalSource"/> are written as a self-cast on that
/// original source, so the NPC has to be the caster or the graph lands on the player.
/// </summary>
/// <remarks>
    /// Service skills (warehouse, store, talk, …) are template flags and are not in
    /// <c>npc_interactions</c>, so they stay player-cast. Authored plots that start from
    /// previous-source / previous-target also stay player-cast: their later events already name
    /// original-target when they mean the NPC.
/// </remarks>
public static class NpcInteractionCastRules
{
    /// <summary>
    /// True when this authored interaction skill's plot is a self-cast on original source, so the
    /// targeted NPC must use it.
    /// </summary>
    public static bool UseTargetNpcAsCaster(
        bool skillIsInAuthoredSet,
        uint rootSourceUpdateMethodId,
        uint rootTargetUpdateMethodId)
    {
        if (!skillIsInAuthoredSet)
            return false;

        return rootSourceUpdateMethodId == (uint)PlotSourceUpdateMethodType.OriginalSource &&
               rootTargetUpdateMethodId == (uint)PlotTargetUpdateMethodType.OriginalSource;
    }

    /// <summary>
    /// Resolves the NPC that should run an authored interaction skill the player just started.
    /// </summary>
    public static bool TryResolveNpcCaster(
        BaseUnit currentCaster,
        BaseUnit target,
        uint skillId,
        SkillTemplate template,
        IReadOnlyList<uint> authoredSkills,
        out Npc npc)
    {
        npc = null;
        if (currentCaster is not Character)
            return false;
        if (target is not Npc candidate)
            return false;
        if (skillId == 0 || !ContainsSkill(authoredSkills, skillId))
            return false;

        var root = template?.Plot?.Tree?.RootNode?.Event;
        if (root == null)
            return false;
        if (!UseTargetNpcAsCaster(true, root.SourceUpdateMethodId, root.TargetUpdateMethodId))
            return false;

        npc = candidate;
        return true;
    }

    /// <summary>
    /// The plot the client is asking to stop: the player's own bar, or the interaction NPC that is
    /// running the authored self-cast on the player's timeline.
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

    private static bool ContainsSkill(IReadOnlyList<uint> skills, uint skillId)
    {
        if (skills == null)
            return false;
        for (var i = 0; i < skills.Count; i++)
        {
            if (skills[i] == skillId)
                return true;
        }

        return false;
    }
}
