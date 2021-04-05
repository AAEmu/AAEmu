using System;

using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

using NLog;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class ExitArchemall : SpecialEffectAction
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

            if (caster is Character character)
            {
                character.DisabledSetPosition = true;

                var ypr = character.MainWorldPosition.World.ToRollPitchYaw();
                character.SendPacket(
                    new SCLoadInstancePacket(
                        1,
                        character.MainWorldPosition.ZoneId,
                        character.MainWorldPosition.World.Position.X,
                        character.MainWorldPosition.World.Position.Y,
                        character.MainWorldPosition.World.Position.Z,
                        ypr.X,
                        ypr.Y,
                        ypr.Z
                    )
                );

                character.Transform = character.MainWorldPosition.Clone();
                character.MainWorldPosition = null;
            }
        }
    }
}
