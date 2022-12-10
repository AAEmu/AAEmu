using System;

using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

using NLog;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class ExitArchemall : SpecialEffectAction
    {
        protected override SpecialType SpecialEffectActionType => SpecialType.ExitArchemall;
        
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
            if (caster is Character) { _log.Debug("Special effects: ExitArchemall value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4); }

            if (caster is Character character)
            {
                character.DisabledSetPosition = true;

                character.SendPacket(
                    new SCLoadInstancePacket(
                        character.MainWorldPosition.InstanceId,
                        character.MainWorldPosition.ZoneId,
                        character.MainWorldPosition.World.Position.X,
                        character.MainWorldPosition.World.Position.Y,
                        character.MainWorldPosition.World.Position.Z,
                        character.MainWorldPosition.World.Rotation.X,
                        character.MainWorldPosition.World.Rotation.Y,
                        character.MainWorldPosition.World.Rotation.Z
                    )
                );

                character.Transform = character.MainWorldPosition.Clone();
                character.MainWorldPosition = null;
            }
        }
    }
}
