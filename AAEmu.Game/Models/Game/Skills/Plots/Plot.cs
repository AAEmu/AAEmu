using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Plots.Tree;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Plots;

public class Plot
{
    public uint Id { get; set; }
    public uint TargetTypeId { get; set; }

    // Probably not needed anymore
    public PlotEventTemplate EventTemplate { get; set; }

    public PlotTree Tree { get; set; }

    public async Task RunAsync(BaseUnit caster, SkillCaster casterCaster, BaseUnit target, SkillCastTarget targetCaster,
        SkillObject skillObject, Skill skill, ushort castTlId)
    {
        if (caster is not Unit casterUnit)
            return;

        // New cast-time or channel plot while the previous one is still on the bar: cancel so
        // anims / projectiles do not stack. Sport-fish holds replace each other immediately but
        // must not tear down the rod channel (plots 809 / 821).
        var prev = casterUnit.ActivePlotState;
        if (prev != null && !ReferenceEquals(prev.ActiveSkill, skill))
        {
            var incomingTags = SkillManager.Instance.GetSkillTags(skill.Id);
            var prevSkill = prev.ActiveSkill;
            var prevTags = prevSkill != null ? SkillManager.Instance.GetSkillTags(prevSkill.Id) : null;
            var incomingHold = SportFishCombat.IsFishingHoldSkill(skill.Template.TargetType, incomingTags);
            var prevHold = prevSkill?.Template != null &&
                           SportFishCombat.IsFishingHoldSkill(prevSkill.Template.TargetType, prevTags);
            var incomingCombo = skill.Template != null &&
                                SkillCastOverlapRules.IsInstantComboHit(
                                    skill.Template.CastingTime, skill.Template.CustomGcd);
            var incomingSame = prevSkill != null && prevSkill.Id == skill.Id;
            var previousIsFollowUp = prevSkill != null &&
                                     SkillComboRules.IsComboFollowUpOf(
                                         skill.Id, prevSkill.Id, SkillComboRules.NextFollowUp);
            if (SkillCastOverlapRules.ShouldCancelPreviousPlot(
                    prev.IsCasting || prev.IsChanneling,
                    incomingCombo,
                    incomingSame,
                    SportFishCombat.ShouldCancelPreviousPlot(
                        prev.IsCasting || prev.IsChanneling,
                        prevHold,
                        incomingHold),
                    previousIsFollowUp))
                prev.RequestCancellation();
        }

        var state = new PlotState(caster, casterCaster, target, targetCaster, skillObject, skill, castTlId);
        casterUnit.ActivePlotState = state;
        skill.ActivePlotState = state;

        if (PlotEndRules.ShouldEndWithoutTree(Tree))
        {
            // 67 plots in 10.0.2.13 have no position-1 plot_events row, so PlotManager built no tree for
            // them while 30 skills still cast them (13499 → 47, 16728-16745 → 283-300, ...). Every call
            // site runs this from Task.Run, so the null-tree dereference was swallowed as an unobserved
            // task exception and the skill never ended. PlotManager warns about the count once at load;
            // per cast there is nothing to say.
            if (PlotEndRules.OwnsSkillEnd(skill.Template?.PlotOnly ?? false, skill.ForcePlotGraphOnly))
            {
                // The plot owns the skill end here (13499, 36858), so it runs the sequence the tree
                // itself finishes with.
                PlotTree.EndPlotWithoutTree(state);
            }
            else
            {
                // Skill.Use carries on to cast, fire and end this skill itself (Skill.cs:331), so the
                // plot only drops its state. Ending it here would release the TlId and arm the cooldown
                // from under a cast that is still running.
                PlotTree.DropPlotState(state);
            }
        }
        else
        {
            // I am guessing we want to do something here to run it in a thread, or at least using Async
            await Tree.ExecuteAsync(state);
        }

        if (casterCaster is SkillItem skillItem && caster is Character player && skillItem.SkillSourceItem != null)
        {
            // Trigger item use if not cancelled
            if (!state.CancellationRequested())
                player.ItemUse(skillItem.SkillSourceItem);
            // Free the item from lock
            player.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.ItemUnlock, new ItemUpdate(skillItem.SkillSourceItem), []));
        }
    }
}
