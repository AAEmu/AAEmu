using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Tag-based buff immunity and buff prerequisites, both authored in tables the 10.0.2.13 content
/// database still ships and neither of which was loaded anywhere:
/// </summary>
/// <remarks>
/// <para>
/// <c>tagged_immune_buffs(buff_id, buff_tag_id)</c> — 2 645 rows over 1 980 distinct buffs. While
/// <c>buff_id</c> is active on a unit, a candidate buff carrying <c>buff_tag_id</c> must be refused.
/// Examples read through the <c>tags</c> table: 93 동결 (freeze) refuses tag 919 차가운 발걸음,
/// 131 무적 (invincible) refuses tag 216 무적 면역, 82 대지의 손아귀 refuses tag 10 떠있음. 283 of those
/// rows name a tag the granting buff carries itself, which is the classic "a frozen unit cannot be
/// frozen again" case and is intended: the refusal is per tag, not per buff id.
/// </para>
/// <para>
/// <c>tagged_require_buffs(buff_id, buff_tag_id)</c> — 309 rows. The inverse: buff <c>buff_id</c> may
/// only apply when the target already carries <c>buff_tag_id</c>. 4627 가벼운 발걸음 requires tag 831
/// 무겁다 (carried by buff 1454), 21369 선장의 보호 requires tag 3258 순항선, 20111 무적 비행 requires
/// tag 2841 불사조 날틀.
/// </para>
/// <para>
/// The <c>immune_except_*</c> columns on <c>buffs</c> qualify the grant and therefore live on the
/// buff that is doing the refusing, which is why <see cref="IsRefusedByTagImmunity"/> reads them off
/// the active buff and not off the candidate. Verified against the data: of the 19 buffs with
/// <c>immune_except_creator='t'</c>, 5936/5937/5938 (the ride buffs 속이 거북한 거북, 까만 까마귀,
/// 윙크하는 인큐버스) are immunity granters for tag 216 and are the rows that matter.
/// </para>
/// <para>
/// Deliberately out of scope, and not silently ignored: <c>immune_damage</c> (11 buffs, e.g. 2071
/// 완벽한 방어 = 2000, 4609 데미지 면역 = 9999999), <c>immune_health</c> (2 buffs, both 0.5: 6865
/// 위태로운 건축물 and the explicitly-named 16189 면역 강화 테스트) and the six <c>*_immortality</c>
/// flags (melee/spell/ranged/siege 60/60/60/62, one_time 10, drowning 57) are damage-side, not
/// buff-side. They have no consumer in this tree, and their exact semantics — flat absorb versus a
/// damage threshold for <c>immune_damage</c>, a hit-point floor for <c>immune_health</c>, and whether
/// an immortality is consumed by the blow it survives — are not established by any code or table here,
/// so wiring a guess into the damage path would silently change lethality. <c>fall_damage_immune</c>
/// and <c>fall_damage_immortality</c> are the exception: the fall-damage path already consumes both in
/// <c>Unit.DoFallDamage</c>.
/// </para>
/// </remarks>
public static class BuffImmunityRules
{
    /// <summary>
    /// Whether a buff active on the target refuses a candidate that carries one of its immune tags.
    /// </summary>
    /// <param name="candidateTags">Tags of the candidate buff (<c>tagged_buffs</c> for its id).</param>
    /// <param name="activeBuffs">The target's active buffs.</param>
    /// <param name="grantedImmunityTags">
    /// Tags a given buff id refuses while it is active (<c>tagged_immune_buffs</c>), looked up by the
    /// active buff's <see cref="BuffTemplate.BuffId"/>.
    /// </param>
    /// <param name="casterObjId">ObjId of the unit applying the candidate, 0 when there is none.</param>
    /// <param name="casterSkillTags">Tags of the skill applying the candidate, empty when it is not a skill cast.</param>
    /// <param name="casterRelationMatches">
    /// Resolves <c>immune_except_creator_relation_id</c> against the caster's relation to the owner.
    /// Called only for a grant whose relation check is on.
    /// </param>
    public static bool IsRefusedByTagImmunity(
        IReadOnlyCollection<uint> candidateTags,
        IReadOnlyList<Buff> activeBuffs,
        Func<uint, IReadOnlyList<uint>> grantedImmunityTags,
        uint casterObjId,
        IReadOnlyCollection<uint> casterSkillTags,
        Func<uint, bool> casterRelationMatches)
    {
        if (candidateTags == null || candidateTags.Count == 0 || activeBuffs == null || grantedImmunityTags == null)
            return false;

        foreach (var active in activeBuffs)
        {
            var template = active?.Template;
            if (template == null)
                continue;

            var granting = grantedImmunityTags(template.BuffId);
            if (granting == null || granting.Count == 0)
                continue;

            if (!CarriesAnyTag(candidateTags, granting))
                continue;

            if (IsExemptFromGrant(template, active.Caster?.ObjId ?? 0, casterObjId, casterSkillTags,
                    casterRelationMatches))
                continue;

            return true;
        }

        return false;
    }

    /// <summary>
    /// The first <c>tagged_require_buffs</c> prerequisite the target does not meet, or 0 when it meets
    /// all of them.
    /// </summary>
    /// <param name="requiredTagIds">Tags the candidate needs the target to already carry.</param>
    /// <param name="ownerHasTag">Whether the target carries a tag, i.e. has an active buff listed under it.</param>
    public static uint FirstMissingRequiredTag(IReadOnlyCollection<uint> requiredTagIds, Func<uint, bool> ownerHasTag)
    {
        if (requiredTagIds == null || requiredTagIds.Count == 0 || ownerHasTag == null)
            return 0;

        foreach (var tagId in requiredTagIds)
        {
            if (!ownerHasTag(tagId))
                return tagId;
        }

        return 0;
    }

    private static bool CarriesAnyTag(IReadOnlyCollection<uint> candidateTags, IReadOnlyList<uint> grantingTags)
    {
        foreach (var candidateTag in candidateTags)
        {
            for (var i = 0; i < grantingTags.Count; i++)
            {
                if (grantingTags[i] == candidateTag)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the grant steps aside for this application. Each clause is independent — any one of them
    /// lets the candidate through.
    /// </summary>
    /// <param name="immunity">The active buff that grants the immunity, i.e. the one carrying the exception columns.</param>
    /// <param name="creatorObjId">
    /// ObjId of the unit that created the immunity-granting buff (<c>Buff.Caster</c>, the field this tree
    /// models "who applied this buff" with; 0 when it was not a unit, e.g. a doodad cast).
    /// </param>
    /// <param name="casterObjId">ObjId of the unit applying the candidate.</param>
    private static bool IsExemptFromGrant(BuffTemplate immunity, uint creatorObjId, uint casterObjId,
        IReadOnlyCollection<uint> casterSkillTags, Func<uint, bool> casterRelationMatches)
    {
        if (immunity.ImmuneExceptCreator && casterObjId != 0 && casterObjId == creatorObjId)
            return true;

        if (immunity.ImmuneExceptSkillTagId != 0 && casterSkillTags != null &&
            casterSkillTags.Contains(immunity.ImmuneExceptSkillTagId))
            return true;

        if (immunity.ImmuneExceptCreatorRelationCheck && casterRelationMatches != null &&
            casterRelationMatches(immunity.ImmuneExceptCreatorRelationId))
            return true;

        return false;
    }
}
