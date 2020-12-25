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
                var pos = target.Transform.CloneDetached();
                var distance = (float)value1 / 1000f;
                pos.Local.AddDistanceToFront(distance);
                // TODO: does the 0f here need to be distance ?
                character.SendPacket(new SCBlinkUnitPacket(caster.ObjId, 0f, (float)MathUtil.RadianToDegree(pos.Local.ToYawPitchRoll().Z), pos.Local.Position.X, pos.Local.Position.Y, pos.Local.Position.Z));
            }
        }
    }
}
