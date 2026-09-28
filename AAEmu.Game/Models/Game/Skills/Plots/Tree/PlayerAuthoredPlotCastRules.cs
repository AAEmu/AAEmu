using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills.Plots.Tree;

/// <summary>
/// Whether the plot graph of a cast owns the end of that cast, so the client sees one timeline from the key
/// press to the last event of the graph.
/// </summary>
/// <remarks>
/// <para>
/// A player press is acknowledged with <c>SCSkillStarted</c>, and the client keys every later packet of that
/// cast - <c>SCSkillFired</c>, the plot events, the plot end, <c>SCSkillEnded</c> - against the timeline id
/// the Started carried. <c>Skill.Use</c> only sends that Started on the cast-time branch, so a cast with no
/// casting time sends it nowhere. When such a cast also carries a plot, the graph is the only thing that
/// moves the client's bar forward, and it runs on its own thread after <c>Skill.Use</c> has already returned:
/// the cast's own <c>SCSkillFired</c> / <c>SCSkillEnded</c> leave while the graph is still on the client's
/// timeline. The client is then holding a timeline the server has already closed, and the graph's events
/// arrive against a cast that is over - the player presses the action and nothing completes, which is what a
/// picked NPC-interaction action did.
/// </para>
/// <para>
/// So for a cast of this shape the graph ends the cast instead: the Started goes out with the id the graph
/// is launched on, and the graph's own end runs the ordinary end sequence. The bar is then one continuous
/// timeline, and a cancel of it lands on a plot state that is still there to cancel.
/// </para>
/// <para>
/// Scoped to the two conditions that are actually measured, so the change cannot reach the ordinary ability
/// kit: the cast has to be a player cast of an action the NPC's authored interaction set offered
/// (<c>NpcInteractionCastRules.IsOfferedInteractionSkill</c>), and it has to be a cast with no casting time.
/// A cast-time skill already sends its Started from <c>Skill.Use</c> and a <c>plot_only</c> skill already
/// returns from it without an <c>EndSkill</c> behind it, so neither is missing anything and neither is
/// touched.
/// </para>
/// </remarks>
public static class PlayerAuthoredPlotCastRules
{
    /// <summary>
    /// Whether the graph of this cast runs the end of the cast. A cast whose end is already owned by the
    /// graph (<c>plot_only</c>, or the hold/reel kit's <c>ForcePlotGraphOnly</c>) is excluded: standing its
    /// own <c>EndSkill</c> down for those would change nothing.
    /// </summary>
    public static bool PlotOwnsCastEnd(
        bool isPlayerCast,
        bool isOfferedInteractionAction,
        SkillTemplate template,
        bool forcePlotGraphOnly)
    {
        if (!isPlayerCast || !isOfferedInteractionAction)
            return false;
        if (template?.Plot == null)
            return false;
        if (template.PlotOnly || forcePlotGraphOnly)
            return false;
        // Only a cast-time skill sends SCSkillStarted from Skill.Use. A cast with no casting time falls
        // straight through to Cast(), so this is the whole of the missing-Started condition.
        return template.CastingTime <= 0;
    }
}
