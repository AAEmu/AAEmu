using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Plots.Tree;
using AAEmu.Game.Models.Game.Skills.Plots.Type;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Plots
{
    public class PlotEventEffect
    {
        public int Position { get; set; }
        public PlotEffectSource SourceId { get; set; }
        public PlotEffectTarget TargetId { get; set; }
        public uint ActualId { get; set; }
        public string ActualType { get; set; }
        
        public void ApplyEffect(PlotState state, PlotTargetInfo targetInfo, PlotEventTemplate evt, ref byte flag)
        {
            var template = SkillManager.Instance.GetEffectTemplate(ActualId, ActualType);

            if (template is BuffEffect)
                flag = 6; //idk what this does?  

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

                template.Apply(
                    source,
                    state.CasterCaster,
                    target,
                    state.TargetCaster,
                    new CastPlot(evt.PlotId, state.ActiveSkill.TlId, evt.Id, state.ActiveSkill.Template.Id), state.ActiveSkill, state.SkillObject, DateTime.Now);
            }
        }
    }
}
