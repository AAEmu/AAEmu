using System;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class TeleportToUnit : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.TeleportToUnit;

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
        if (caster is Character) { Logger.Debug("Special effects: TeleportToUnit value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4); }

        if (target == null)
        {
            //this shouldn't happen?
            return;
        }

        // value1 is the minimum distance you will be placed from the target and value2 is the maximum.
        // value3 is the minimum rotation in degrees relative to the target's orientation, and value4 is the maximum.
        // E.g. if rotationDegrees is 0, you will be placed in front of the target, and if rotationDegrees is 180 you will be placed behind the target.

        var distance = Rand.Next(value1, value2);
        var rotationDegrees = Rand.Next(value3, value4);

        var targetPosition = target.Transform.World.Position;
        var (endX, endY) = MathUtil.AddDistanceToFrontDeg(distance / 1000f, targetPosition.X, targetPosition.Y, target.Transform.World.ToRollPitchYawDegrees().Z + 90 + rotationDegrees);

        switch (caster)
        {
            case Character character:
                character.SendPacket(new SCUnitBlinkPacket(caster.ObjId, 0f, 0f, endX, endY, targetPosition.Z));
                break;
            case Npc npc:
                npc.MoveTowards(targetPosition, 10000);
                npc.StopMovement();
                break;
        }
    }
}
