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
        // Guild worldgate UI: pick a saved base, cast, hop there. Type 30 carries the portal id
        // (and a bc for the device); this is not a personal book enter/exit spawn.
        if (caster is Character character && skillObject is SkillObjectExpeditionPortal portal)
            ExpeditionActivityServices.Get().TeleportToPortal(character, portal.PortalId);
    }
}
