using System.Numerics;

using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.StaticValues;

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
        Logger.Debug($"Special effects: Blink caster={caster?.ObjId} value1={value1}, value2={value2}, value3={value3}, value4={value4}");

        if (caster is Character character)
        {
            using var newPos = character.Transform.CloneDetached();
            newPos.Local.AddDistanceToFront(value1);
            if (character.IsRiding)
            {
                var mateList = character.ParentWorld.MateManager.GetActiveMates(character.Id);
                foreach (var mate in mateList)
                {
                    character.ParentWorld.MateManager.UnMountMate(character, mate.TlId, AttachPointKind.Driver, AttachUnitReason.None);
                }
            }
            character.SendPacket(new SCBlinkUnitPacket(caster.ObjId, value1, value2, newPos.Local.Position.X, newPos.Local.Position.Y, newPos.Local.Position.Z));
        }
        else if (caster is Npc npc)
        {
            var oldPosition = npc.Transform.Local.ClonePosition();

            using var newPos = npc.Transform.CloneDetached();
            newPos.Local.AddDistanceToFront(value1);

            var endX = newPos.Local.Position.X;
            var endY = newPos.Local.Position.Y;
            var endZ = newPos.Local.Position.Z;

            var groundZ = npc.ParentWorld.Template.GeoData.GetHeight(new Vector3(endX, endY, endZ));
            if (groundZ > 0 && Math.Abs(endZ - groundZ) < 5f)
                endZ = groundZ;

            npc.Transform.Local.SetPosition(endX, endY, endZ);

            npc.BroadcastPacket(new SCBlinkUnitPacket(npc.ObjId, value1, value2, endX, endY, endZ), false);

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
        }
    }
}
