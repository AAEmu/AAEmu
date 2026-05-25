using System.Numerics;

using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class KnockBack : SpecialEffectAction
{
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
        Logger.Debug("Special effects: KnockBack caster={0}, target={1}, value1={2}, value2={3}, value3={4}, value4={5}",
            caster?.ObjId, target?.ObjId, value1, value2, value3, value4);

        if (caster == null || target == null)
            return;

        if (target.Buffs.HasEffectsMatchingCondition(e => e.Template.KnockbackImmune || e.Template.NonPushable))
            return;

        if (target is Npc npcTarget && npcTarget.Template.NonPushableByActor)
            return;

        var knockDistMeters = value1 / 1000f;
        if (knockDistMeters <= 0f)
            knockDistMeters = 3f;

        var casterPos = caster.Transform.World.Position;
        var targetPos = target.Transform.World.Position;
        var direction = targetPos - casterPos;
        direction.Z = 0;
        if (direction.LengthSquared() < 0.01f)
            direction = new Vector3(1, 0, 0);
        direction = Vector3.Normalize(direction);

        var endPos = targetPos + direction * knockDistMeters;

        var heightBoost = value3 / 1000f;
        endPos.Z = targetPos.Z + heightBoost;

        // Characters: client handles knockback natively from the skill/buff data.
        // Sending the packet from the server causes double displacement.
        if (target is Character)
            return;

        if (target is Npc npc)
        {
            ApplyKnockbackToNpc(npc, endPos);
        }
    }

    internal static void ApplyKnockbackToNpc(Npc npc, Vector3 endPos)
    {
        // 600ms suppression so the client knockback animation plays out and a follow-up
        // knockback isn't overridden by the NPC walking back.
        npc.DisplacedUntil = DateTime.UtcNow.AddMilliseconds(600);

        var oldPosition = npc.Transform.Local.ClonePosition();

        var groundZ = npc.ParentWorld.Template.GeoData.GetHeight(new Vector3(endPos.X, endPos.Y, endPos.Z));
        if (groundZ > 0 && Math.Abs(endPos.Z - groundZ) < 5f)
            endPos.Z = groundZ;

        npc.Transform.Local.SetPosition(endPos.X, endPos.Y, endPos.Z);

        var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);
        moveType.X = endPos.X;
        moveType.Y = endPos.Y;
        moveType.Z = endPos.Z;
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
