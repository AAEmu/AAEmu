using System;

using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

using NLog;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class Blink : SpecialEffectAction
    {
        protected override SpecialType SpecialEffectActionType => SpecialType.Blink;
        
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
            if (caster is Character) { _log.Debug("Special effects: Blink value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4); }

            if (caster is Character character)
            {
                //character.SendMessage("From: " + character.Transform.ToString());
                var newPos = character.Transform.CloneDetached(); 
                newPos.Local.AddDistanceToFront(value1);
                //var (endX, endY) = MathUtil.AddDistanceToFront(value1, character.Transform.World.Position.X, character.Transform.World.Position.Y, (sbyte)value2);
                //var endZ = character.Transform.World.Position.Z;
                character.SendPacket(new SCBlinkUnitPacket(caster.ObjId, value1, value2, newPos.Local.Position.X, newPos.Local.Position.Y, newPos.Local.Position.Z));
                //character.SendMessage("To: " + newPos.ToString());
                //character.SendPacket(new SCBlinkUnitPacket(caster.ObjId, value1, value2, endX, endY, endZ));
            }
        }
    }
}
