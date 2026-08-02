using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Slaves;

/// <summary>
/// Growling/Eznan sail fold UI is driven by <c>buff_mount_skills</c>:
/// fold-anim buff (tag 1770, e.g. 14099) enables the unfurl mount skill;
/// unfurl-anim buff (tag 137, e.g. 14351) enables the fold mount skill.
/// Those anim buffs must be mutually exclusive or the client shows red/disabled
/// sail icons and "thinks" the sails are folded when they are not.
/// </summary>
public static class SailFoldBuffs
{
    /// <summary>돛 펴기 애니메이션 강화 해제 — fold-anim family (buff_mount enables unfurl).</summary>
    public const uint FoldAnimTagId = 1770;

    /// <summary>Unfurl-anim family (buff_mount enables fold).</summary>
    public const uint UnfurlAnimTagId = 137;

    /// <summary>으르렁 전방 사각돛 접힘 state on hull (dispelled by unfurl).</summary>
    public const uint GrowlingFrontFoldedStateTagId = 2320;

    public static void ApplyAnimExclusivity(BaseUnit target, uint newBuffId)
    {
        if (target == null || newBuffId == 0)
            return;

        var tags = SkillManager.Instance.GetBuffTags(newBuffId);
        if (tags == null || tags.Count == 0)
            return;

        if (tags.Contains(FoldAnimTagId))
            target.Buffs.RemoveBuffs(UnfurlAnimTagId, 32);
        if (tags.Contains(UnfurlAnimTagId))
            target.Buffs.RemoveBuffs(FoldAnimTagId, 32);
    }

    /// <summary>
    /// Unfurl's Dispel hits the hull state tag only. Also clear fold-anim on the equipment sail
    /// (skill caster) so buff_mount swaps back to fold.
    /// </summary>
    public static void OnFoldStateDispelled(BaseUnit caster, uint dispelledBuffTagId)
    {
        if (caster == null)
            return;
        if (dispelledBuffTagId != GrowlingFrontFoldedStateTagId && dispelledBuffTagId != 2265)
            return;
        caster.Buffs.RemoveBuffs(FoldAnimTagId, 32);
    }
}
