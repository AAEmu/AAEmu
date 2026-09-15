namespace AAEmu.Game.Models.Game.Skills.Buffs;

/// <summary>
/// The four units a <c>buff_triggers</c> row can name in its <c>source_agent_id</c> and
/// <c>target_agent_id</c> columns: which unit supplies the effect's source and which one it is applied to.
/// </summary>
/// <remarks>
/// Names and ids are the content database's own <c>enum_buff_trigger_agents</c> table, not a naming choice
/// made here. They are also not the same numbering as the plot engine's source/target kinds
/// (<see cref="Plots.Type.PlotEffectSource"/> starts at OriginalSource = 1) - do not share constants
/// between the two.
/// </remarks>
public enum BuffTriggerAgent
{
    /// <summary>The unit the buff sits on. This is what a row with no agent columns means.</summary>
    Owner = 0,

    /// <summary>
    /// The unit that caused the event: the attacker of an <c>attacked</c>/<c>damaged</c> trigger, the
    /// killer of a <c>death</c> one, the victim of an <c>attack</c>/<c>damage</c> one. Events no unit
    /// caused (timeout, dispelled, started) fall back to <see cref="OriginalSource"/>.
    /// </summary>
    Source = 1,

    /// <summary>
    /// The unit the event acted on: the victim of an <c>attack</c>/<c>damage</c> trigger, the dead unit
    /// of a <c>death</c> one. Falls back to <see cref="Owner"/>, which is what those events already
    /// carry when the raiser does not name the other side.
    /// </summary>
    Target = 2,

    /// <summary>
    /// The unit that applied this buff, i.e. the caster of the skill the buff came from. Differs from
    /// <see cref="Owner"/> on every debuff, and from <see cref="Source"/> whenever someone other than the
    /// applier is hitting the owner.
    /// </summary>
    OriginalSource = 3
}