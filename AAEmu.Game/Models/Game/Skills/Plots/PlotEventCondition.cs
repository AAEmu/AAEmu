using AAEmu.Game.Models.Game.Skills.Plots.Tree;
using AAEmu.Game.Models.Game.Skills.Plots.Type;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Models.Game.Skills.Plots;

public class PlotEventCondition
{
    public PlotCondition Condition { get; set; }
    public int Position { get; set; }
    public PlotEffectSource SourceId { get; set; }
    public PlotEffectTarget TargetId { get; set; }
    public bool NotifyFailure { get; set; } // 10.0.2.13: plot_event_conditions.notify_failure present again

    public bool CheckCondition(PlotState state, PlotTargetInfo targetInfo)
    {
        if (GetConditionResult(state, targetInfo, this))
            return true;

        // notify_failure marks the conditions whose failure the player is meant to see — the "you cannot do
        // that here" line rather than a silent no-op. The column is present in 10.0.2.13 and the loader now
        // binds it, so honour it: anything else leaves a skill that simply does nothing with no reason given.
        if (NotifyFailure && state?.Caster is Char.Character character)
            character.SendErrorMessage(ErrorMessageType.SkillCannotUseHere);

        return false;

    }

    private bool GetConditionResult(PlotState state, PlotTargetInfo targetInfo, PlotEventCondition condition)
    {
        var not = condition.Condition.NotCondition;
        //Check if condition was cached
        // if (instance.UseConditionCache(condition.Condition))
        // {
        //     var cacheResult = instance.GetConditionCacheResult(condition.Condition);
        //     //Apply not condition
        //     cacheResult = not ? !cacheResult : cacheResult;
        //
        //     return cacheResult;
        // }

        Unit source;
        switch (SourceId)
        {
            case PlotEffectSource.OriginalSource:
                source = state.Caster;
                break;
            case PlotEffectSource.OriginalTarget:
                source = (Unit)state.Target;
                break;
            case PlotEffectSource.Source:
                source = (Unit)targetInfo.Source;
                break;
            case PlotEffectSource.Target:
                source = (Unit)targetInfo.Target;
                break;
            default:
                throw new InvalidOperationException("This can't happen");
        }

        var result = true;

        if (targetInfo.EffectedTargets.Count == 0)
        {
            // With no hits the loop below never executes, so the method returns its initial `true` —
            // a "did we find a target?" gate reports SUCCESS precisely when nothing was found. The
            // plot then takes the success edge, whose children are per_target and expand to nothing,
            // the queue drains and SCPlotEnded fires. That is what made Crashing Wave (plot 3523)
            // dead-end at event 48423. Evaluate the condition once against the non-per-target unit.
            var fallbackTarget = TargetId switch
            {
                PlotEffectTarget.OriginalSource => state.Caster,
                PlotEffectTarget.OriginalTarget => state.Target,
                PlotEffectTarget.Source => targetInfo.Source,
                _ => targetInfo.Target
            };
            return condition.Condition.Check(source, state.CasterCaster, fallbackTarget,
                state.TargetCaster, state.SkillObject, state.ActiveSkill);
        }

        foreach (var newTarget in targetInfo.EffectedTargets)
        {
            BaseUnit target;
            switch (TargetId)
            {
                case PlotEffectTarget.OriginalSource:
                    target = state.Caster;
                    break;
                case PlotEffectTarget.OriginalTarget:
                    target = state.Target;
                    break;
                case PlotEffectTarget.Source:
                    target = targetInfo.Source;
                    break;
                case PlotEffectTarget.Target:
                    target = newTarget;
                    break;
                case PlotEffectTarget.Location:
                    target = targetInfo.Target;
                    break;
                default:
                    throw new InvalidOperationException("This can't happen");
            }

            if (condition.Condition.Check(source, state.CasterCaster, target,
                state.TargetCaster, state.SkillObject, state.ActiveSkill))
            {
                continue;
            }

            result = false;
            break;
        }

        //Check 
        // var result = condition.Condition.Check(instance.Caster, instance.CasterCaster, instance.Target, instance.TargetCaster, instance.SkillObject, condition);
        // if (result)
        // {
        //     //We need to undo the not condition to store in cache
        //     // instance.UpdateConditionCache(condition.Condition, !not);
        //     return true;
        // }
        //
        // // instance.UpdateConditionCache(condition.Condition, not);
        // return false;
        return result;
    }
}
