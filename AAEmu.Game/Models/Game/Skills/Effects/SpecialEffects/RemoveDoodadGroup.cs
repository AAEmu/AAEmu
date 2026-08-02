using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class RemoveDoodadGroup : SpecialEffectAction
{
    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int doodadGroupId,
        int radiusMillimeters,
        int value3,
        int value4)
    {
        if (doodadGroupId <= 0 ||
            !DoodadRemovalResolver.TryGetCandidates(caster, target, skill, radiusMillimeters, out var candidates))
            return;

        var groupId = (uint)doodadGroupId;
        foreach (var doodad in candidates.Where(doodad => doodad.Template?.GroupId == groupId).ToArray())
            doodad.Delete();
    }
}
