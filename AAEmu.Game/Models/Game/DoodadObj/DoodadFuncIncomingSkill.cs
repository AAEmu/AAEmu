using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.DoodadObj.Templates;

namespace AAEmu.Game.Models.Game.DoodadObj;

/// <summary>
/// Matches an incoming skill to a doodad func when <see cref="DoodadFunc.SkillId"/> is empty
/// and the real skill lives on the func template.
/// </summary>
/// <remarks>
/// <c>doodad_funcs.func_skill_id</c> is unset on every <see cref="DoodadFuncSkillHit"/> row
/// in 10.0.2.13 (chum, harvest hits, school count). The skill is only on
/// <c>doodad_func_skill_hits.skill_id</c>. Without this match, <c>GetFunc</c> falls through to
/// the first skill-less func in the phase, so only the first chum skill of a school ever
/// advanced — squid / horse mackerel / saury / sardine never did.
/// </remarks>
public static class DoodadFuncIncomingSkill
{
    public static bool TemplateAccepts(DoodadFuncTemplate template, uint skillId)
    {
        if (template == null || skillId == 0)
            return false;

        return template switch
        {
            DoodadFuncFakeUse { FakeSkillId: > 0 } fakeUse => fakeUse.FakeSkillId == skillId,
            DoodadFuncConditionalUse { FakeSkillId: > 0 } conditionalUse => conditionalUse.FakeSkillId == skillId,
            DoodadFuncConditionalUse { SkillId: > 0 } conditionalUse => conditionalUse.SkillId == skillId,
            DoodadFuncUse { SkillId: > 0 } use => use.SkillId == skillId,
            DoodadFuncSkillHit { SkillId: > 0 } skillHit => skillHit.SkillId == skillId,
            // Evidence pickup: doodad_funcs.func_skill_id is unset on these rows too, so without
            // this arm GetFunc would only reach the loot func through the skill-less fallback.
            DoodadFuncEvidenceItemLoot { SkillId: > 0 } evidenceLoot => evidenceLoot.SkillId == skillId,
            _ => false
        };
    }

    /// <summary>
    /// Whether a template reserves its func for an incoming skill. This distinguishes a generic
    /// skill-less interaction from a skill-gated template whose declared skill did not match.
    /// </summary>
    public static bool HasDeclaredIncomingSkill(DoodadFuncTemplate template) => template switch
    {
        DoodadFuncFakeUse { FakeSkillId: > 0 } => true,
        DoodadFuncConditionalUse { FakeSkillId: > 0 } => true,
        DoodadFuncConditionalUse { SkillId: > 0 } => true,
        DoodadFuncUse { SkillId: > 0 } => true,
        DoodadFuncSkillHit { SkillId: > 0 } => true,
        DoodadFuncEvidenceItemLoot { SkillId: > 0 } => true,
        _ => false
    };
}
