using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// Deletes every doodad the effect reaches, with no template or group filter — 고전포 발사 15235, 무모한 돌진
/// 21538 and the four doodad-tick rows use it to clear the barriers around them.
/// </summary>
/// <remarks>
/// content, 10.0.2.13 game_decrypted: 52 <c>special_effects</c> rows of type 140, 7 of them reachable (3
/// <c>skill_effects</c>: 15235 at 15,000 mm, 21538 at 10,000 mm and the test skill 두대드 제거 50349 at
/// 5,000 mm, plus 4 <c>buff_tick_effects</c> on buffs 24163 / 26865 / 27095 / 27708 at 10,000-15,000 mm).
/// <c>value1</c> is the radius in millimetres — the same unit <c>remove_doodad</c> (58) and
/// <c>remove_doodad_group</c> (130) carry in <c>value2</c>, where <c>value1</c> holds their doodad template
/// or group id instead and the radius runs from 0 to 2,500,000 mm. <c>value2</c> here is 1 on 50 rows and 0
/// on two (25435 and 50349) and no shipped row establishes what it means, so it is not read.
/// </remarks>
public class RemoveAllDoodad : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.RemoveAllDoodad;

    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int value1,
        int value2,
        int value3,
        int value4)
    {
        if (!DoodadRemovalResolver.TryGetCandidates(caster, target, skill, value1, out var candidates))
            return;

        foreach (var doodad in candidates.Where(doodad => RemoveAllDoodadRules.ShouldRemove(doodad.OwnerType)).ToArray())
            doodad.Delete();
    }
}
