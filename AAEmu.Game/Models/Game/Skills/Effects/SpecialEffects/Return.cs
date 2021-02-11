using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

using NLog;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class Return : SpecialEffectAction
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        public override void Execute(Unit caster,
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
            if (target is Character character)
            {
                var targetDistrictPortal = PortalManager.Instance.GetPortalBySubZoneId(character.ReturnDictrictId);

                if (targetDistrictPortal != null)
                {
                    character.DisabledSetPosition = true;
                    character.SendPacket(new SCTeleportUnitPacket(0, 0, targetDistrictPortal.X, targetDistrictPortal.Y, targetDistrictPortal.Z, 0));
                }
                
            }

            _log.Warn("value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4);
        }
    }
}
