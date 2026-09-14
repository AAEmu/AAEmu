using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public sealed class TeleportExpeditionPortal : SpecialEffectAction
{
    public override void Execute(BaseUnit caster, SkillCaster casterObj, BaseUnit target,
        SkillCastTarget targetObj, CastAction castObj, Skill skill, SkillObject skillObject, DateTime time,
        int value1, int value2, int value3, int value4)
    {
        if (caster is Character character && skillObject is SkillObjectExpeditionPortal portal &&
            skill?.Template?.Id == portal.SkillId)
            ExpeditionActivityServices.Get().TeleportToPortal(character, portal.PortalId);
    }
}
