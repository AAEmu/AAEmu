using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Plots.Tree;
using AAEmu.Game.Models.Game.Skills.Plots.Type;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Plots;

public class PlotEventEffect
{
    public int Position { get; set; }
    public PlotEffectSource SourceId { get; set; }
    public PlotEffectTarget TargetId { get; set; }
    public uint ActualId { get; set; }
    public string ActualType { get; set; }

    public void ApplyEffect(PlotState state, PlotTargetInfo targetInfo, PlotEventTemplate evt, ref byte flag,
        bool channeled = false, CompressedGamePackets gamePackets = null,
        Action<Action> deferUntilPlotEventProcessed = null)
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

        // Per-hit Target effects need EffectedTargets. Location / Original* / Source resolve
        // independently — e.g. SetVariable op 12 on plot 5796 event 52251 (target Location)
        // must still run when the area search finds nobody so Variables[index] gets 0.
        if (targetInfo.EffectedTargets.Count == 0 && TargetId != PlotEffectTarget.Target)
        {
            ApplyToResolvedTarget(source, ResolveFixedTarget(state, targetInfo), state, evt, buffEffect,
                channeled, gamePackets, deferUntilPlotEventProcessed, template);
            return;
        }

        foreach (var newTarget in targetInfo.EffectedTargets)
        {
            var target = TargetId == PlotEffectTarget.Target
                ? newTarget
                : ResolveFixedTarget(state, targetInfo);

            ApplyToResolvedTarget(source, target, state, evt, buffEffect, channeled, gamePackets,
                deferUntilPlotEventProcessed, template);
        }
    }

    private BaseUnit ResolveFixedTarget(PlotState state, PlotTargetInfo targetInfo) =>
        TargetId switch
        {
            PlotEffectTarget.OriginalSource => state.Caster,
            PlotEffectTarget.OriginalTarget => state.Target,
            PlotEffectTarget.Source => targetInfo.Source,
            PlotEffectTarget.Location => targetInfo.Target,
            PlotEffectTarget.Target => targetInfo.Target,
            _ => throw new InvalidOperationException("This can't happen")
        };

    private void ApplyToResolvedTarget(
        BaseUnit source,
        BaseUnit target,
        PlotState state,
        PlotEventTemplate evt,
        BuffEffect buffEffect,
        bool channeled,
        CompressedGamePackets gamePackets,
        Action<Action> deferUntilPlotEventProcessed,
        EffectTemplate template)
    {
        if (template == null || target == null)
            return;

        if (channeled && buffEffect != null)
            state.ChanneledBuffs.Add((target, buffEffect.BuffId));

        template.Apply(
            source,
            state.CasterCaster,
            target,
            state.TargetCaster,
            new CastPlot(evt.PlotId, state.ActiveSkill.TlId, evt.Id, state.ActiveSkill.Template.Id),
            new EffectSource(state.ActiveSkill, deferUntilPlotEventProcessed),
            state.SkillObject,
            DateTime.UtcNow,
            gamePackets);
    }
}
