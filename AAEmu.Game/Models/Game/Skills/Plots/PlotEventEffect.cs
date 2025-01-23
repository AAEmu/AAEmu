using System;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Plots.Tree;
using AAEmu.Game.Models.Game.Skills.Plots.Type;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Plots;

public class PlotEventEffect
{
    public int Position { get; set; }
    public PlotEffectSource SourceId { get; set; }
    public PlotEffectTarget TargetId { get; set; }
    public uint ActualId { get; set; }
    public string ActualType { get; set; }

    public void ApplyEffect(PlotState state, PlotTargetInfo targetInfo, PlotEventTemplate evt, ref byte flag, bool channeled = false, CompressedGamePackets gamePackets = null)
    {
        var template = SkillManager.Instance.GetEffectTemplate(ActualId, ActualType);

        var buffEffect = template as BuffEffect;
        if (buffEffect != null)
            flag = 2; //We still don't know what this does, but we had it as 6, and it's 2 in our packet sniffing. 

        BaseUnit source;
        switch (SourceId)
        {
            case PlotEffectSource.OriginalSource:
                source = state.Caster;
                break;
            case PlotEffectSource.OriginalTarget:
                source = state.Target;
                break;
            case PlotEffectSource.Source:
                source = targetInfo.Source;
                break;
            case PlotEffectSource.Target:
                source = targetInfo.Target;
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

            if (channeled && buffEffect != null)
                state.ChanneledBuffs.Add((target, buffEffect.BuffId));

            if (template == null)
            {
                return;
            }

            template.Apply(
                source,
                state.CasterCaster,
                target,
                state.TargetCaster,
                new CastPlot(evt.PlotId, state.ActiveSkill.TlId, evt.Id, state.ActiveSkill.Template.Id),
                new EffectSource(state.ActiveSkill),
                state.SkillObject,
                DateTime.UtcNow,
                gamePackets);
        }
    }
}
