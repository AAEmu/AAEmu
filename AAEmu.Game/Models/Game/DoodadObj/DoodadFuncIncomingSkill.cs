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
            DoodadFuncUse { SkillId: > 0 } use => use.SkillId == skillId,
            DoodadFuncSkillHit { SkillId: > 0 } skillHit => skillHit.SkillId == skillId,
            _ => false
        };
    }
}
