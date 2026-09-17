using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills.Plots.Tree;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

/// <summary>
/// Forgets the units this plot has already hit, so a <c>hit_once</c> search may pick them again.
/// </summary>
/// <remarks>
/// 10.0.2.13 ships 11 rows in <c>target_history_clear_effects</c> and 8 <c>plot_effects</c> rows naming this
/// type, all of them at position 1 of an event called 대상 초기화 ("target reset", plot 3795) or 발사 실패
/// ("firing failed", plots 4309, 4313, 4475, 4482, 4588, 4592, 5560). That is the retry path: the plot's
/// firing loop marks its target in <see cref="PlotState.HitObjects"/>, and the failure branch has to clear it
/// or the next round would skip the very unit it is aimed at.
/// </remarks>
public class TargetHistoryClearEffect : EffectTemplate
{
    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        var plotState = source?.Skill?.ActivePlotState ?? (caster as Unit)?.ActivePlotState;
        if (plotState == null)
        {
            Logger.Warn("TargetHistoryClearEffect applied outside a plot; nothing to clear");
            return;
        }

        plotState.ClearHitHistory();
    }
}
