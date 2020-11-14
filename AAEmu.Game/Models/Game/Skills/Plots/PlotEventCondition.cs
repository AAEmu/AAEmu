using System;
using AAEmu.Game.Models.Game.Skills.Plots.Tree;
using AAEmu.Game.Models.Game.Skills.Plots.Type;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Plots
{
    public class PlotEventCondition
    {
        public PlotCondition Condition { get; set; }
        public int Position { get; set; }
        public PlotEffectSource SourceId { get; set; }
        public PlotEffectTarget TargetId { get; set; }
        public bool NotifyFailure { get; set; }

        // TODO 1.2 // public bool NotifyFailure { get; set; }

        public bool CheckCondition(PlotState state, PlotTargetInfo targetInfo)
        {
            if (GetConditionResult(state, targetInfo, this))
                return true;

            if (NotifyFailure)
                ;//Maybe do something here?
            
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
                        source = (Unit) state.Target;
                        break;
                    case PlotEffectSource.Source:
                        source = (Unit) targetInfo.Source;
                        break;
                    case PlotEffectSource.Target:
                        source = (Unit) targetInfo.Target;
                        break;
                    default:
                        throw new InvalidOperationException("This can't happen");
                }

                var result = true;
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
                        state.TargetCaster, state.SkillObject, condition))
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
}
