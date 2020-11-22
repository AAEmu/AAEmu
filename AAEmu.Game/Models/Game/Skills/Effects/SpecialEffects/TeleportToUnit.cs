using System;

using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

using NLog;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class TeleportToUnit : SpecialEffectAction
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
            _log.Warn("value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4);
            if (target == null)
            {
                //this shouldn't happen?
                return;
            }
            if (caster is Character character)
            {
                var pos = target.Position;

                var distance = (float)value1 / 1000f;
                var rot = MathUtil.ConvertDegreeToDirection(MathUtil.ConvertDirectionToDegree(pos.RotationZ) + (float)value3);
                var (endX, endY) = MathUtil.AddDistanceToFront(distance, target.Position.X, target.Position.Y, rot);

                character.SendPacket(new SCBlinkUnitPacket(caster.ObjId, 0f, 0f, endX, endY, pos.Z));
                
            }
        }
    }
}
