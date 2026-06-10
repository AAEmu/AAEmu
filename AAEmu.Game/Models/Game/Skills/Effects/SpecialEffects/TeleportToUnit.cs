using System.Numerics;

using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.StaticValues;
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
        Logger.Debug("Special effects: TeleportToUnit caster={0} target={1} value1 {2}, value2 {3}, value3 {4}, value4 {5}",
            caster?.ObjId, target?.ObjId, value1, value2, value3, value4);

        if (target == null)
        {
            //this shouldn't happen?
            return;
        }

        // value1 is the minimum distance you will be placed from the target and value2 is the maximum.
        // value3 is the minimum rotation in degrees relative to the target's orientation, and value4 is the maximum.
        // E.g. if rotationDegrees is 0, you will be placed in front of the target, and if rotationDegrees is 180 you will be placed behind the target.

        var distance = Random.Shared.Next(value1, value2);
        var rotationDegrees = Random.Shared.Next(value3, value4);

        var targetPosition = target.Transform.World.Position;
        var (endX, endY) = MathUtil.AddDistanceToFrontDeg(distance / 1000f, targetPosition.X, targetPosition.Y, target.Transform.World.ToRollPitchYawDegrees().Z + 90 + rotationDegrees);

        switch (caster)
        {
            case Character character:
                character.SendPacket(new SCBlinkUnitPacket(caster.ObjId, 0f, 0f, endX, endY, targetPosition.Z));
                break;
            case Npc npc:
                // Pull / Charge / Leap onto another NPC: replicate the Character blink path
                // server-side. MoveTowards walks the NPC over time and cancels itself with
                // StopMovement, so the original code was effectively a no-op for NPC pulls.
                var oldPosition = npc.Transform.Local.ClonePosition();

                var endZ = targetPosition.Z;
                var groundZ = npc.ParentWorld?.Template.GeoData.GetHeight(new Vector3(endX, endY, endZ)) ?? 0f;
                if (groundZ > 0 && Math.Abs(endZ - groundZ) < 5f)
                    endZ = groundZ;

                npc.Transform.Local.SetPosition(endX, endY, endZ);

                npc.BroadcastPacket(new SCBlinkUnitPacket(npc.ObjId, 0f, 0f, endX, endY, endZ), false);

                var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);
                moveType.X = endX;
                moveType.Y = endY;
                moveType.Z = endZ;
                moveType.VelX = 0;
                moveType.VelY = 0;
                moveType.VelZ = 0;
                moveType.RotationX = 0;
                moveType.RotationY = 0;
                moveType.RotationZ = npc.Transform.Local.ToRollPitchYawSBytesMovement().Item3;
                moveType.Flags = MoveTypeFlags.Stopping | (npc.IsInBattle ? MoveTypeFlags.InCombat : 0);
                moveType.DeltaMovement = new sbyte[3];
                moveType.Stance = npc.CurrentGameStance;
                moveType.Alertness = npc.CurrentAlertness;
                moveType.Time = (uint)(DateTime.UtcNow - DateTime.UtcNow.Date).TotalMilliseconds;
                npc.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), false);

                npc.CheckMovedPosition(oldPosition);
                break;
        }
    }
}
