using System;
using System.Linq;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class ResidentServicePoint : SpecialEffectAction
{
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
        // TODO ...
        if (caster is Character character)
        {
            Logger.Debug("Special effects: ResidentServicePoint value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4);
            var resident = ResidentManager.Instance.GetResidentByZoneId(character.Transform.ZoneId);
            resident.ZonePoint += value1;
            resident.Point += 1; // количество 
            foreach (var member in resident.Members.Where(member => member.Character.Id == character.Id))
            {
                member.ServicePoint += value1;
                character.SendPacket(new SCResidentInfoPacket(resident.ZoneGroupId, member));
            }
        }
    }
}
