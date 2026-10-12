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
        // Client binding is TeleportExpeditionPortal / skill teleport_expedition_portal, but the
        // worldgate open places a walkable enter/exit pair on the device (book-style), not a
        // personal hop. Type 30 carries the saved portal id.
        if (caster is Character character && skillObject is SkillObjectExpeditionPortal portal)
            ExpeditionActivityServices.Get().OpenPortalAtWorldgate(character, portal.PortalId);
    }
}
