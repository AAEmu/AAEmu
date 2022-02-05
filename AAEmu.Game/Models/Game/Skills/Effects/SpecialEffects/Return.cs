using System;

using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Static;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class Return : SpecialEffectAction
    {
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
            _log.Trace("value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4);

            var trp = TeleportReturnPointGameData.Instance.GetTeleportReturnPoint((uint)value1);
            if (trp != null)
            {
                caster.DisabledSetPosition = true;
                caster.SendPacket(new SCTeleportUnitPacket(TeleportReason.MoveToLocation, 0, trp.Position.X, trp.Position.Y, trp.Position.Z, trp.Position.Yaw));
            }
        }
    }
}
