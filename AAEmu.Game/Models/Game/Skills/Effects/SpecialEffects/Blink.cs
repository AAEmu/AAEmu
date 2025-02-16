using System;
using System.Linq;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Static;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class Blink : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.Blink;

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
        if (caster is Character) { Logger.Debug("Special effects: Blink value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4); }

        if (caster is Character character)
        {
            //character.SendMessage("From: " + character.Transform.ToString());
            using var newPos = character.Transform.CloneDetached();
            newPos.Local.AddDistanceToFront(value1);
            //var (endX, endY) = MathUtil.AddDistanceToFront(value1, character.Transform.World.Position.X, character.Transform.World.Position.Y, (sbyte)value2);
            //var endZ = character.Transform.World.Position.Z;
            if (character.IsRiding)
            {
                var mates = MateManager.Instance.GetActiveMates(character.ObjId);
                if (mates != null)
                {
                    foreach (var mate in mates.Where(mate => mate is { MateType: MateType.Ride }))
                    {
                        MateManager.Instance.UnMountMate(character.Connection, mate.TlId, AttachPointKind.Driver, AttachUnitReason.None);
                    }
                }
            }
            character.SendPacket(new SCUnitBlinkPacket(caster.ObjId, value1, value2, newPos.Local.Position.X, newPos.Local.Position.Y, newPos.Local.Position.Z));
            //character.SendMessage("To: " + newPos.ToString());
            //character.SendPacket(new SCUnitBlinkPacket(caster.ObjId, value1, value2, endX, endY, endZ));
        }
    }
}
