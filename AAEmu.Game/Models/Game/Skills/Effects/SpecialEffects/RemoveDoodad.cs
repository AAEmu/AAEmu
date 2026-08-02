using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class RemoveDoodad : SpecialEffectAction
{
    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int doodadTemplateId,
        int radiusMillimeters,
        int value3,
        int value4)
    {
        if (doodadTemplateId <= 0 ||
            !DoodadRemovalResolver.TryGetCandidates(caster, target, skill, radiusMillimeters, out var candidates))
            return;

        var templateId = (uint)doodadTemplateId;
        foreach (var doodad in candidates.Where(doodad => doodad.TemplateId == templateId).ToArray())
            doodad.Delete();
    }
}
