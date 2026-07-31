using System.Numerics;

using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class ImpulseEffect : EffectTemplate
{
    public float VelImpulseX { get; set; }
    public float VelImpulseY { get; set; }
    public float VelImpulseZ { get; set; }
    public float AngvelImpulseX { get; set; }
    public float AngvelImpulseY { get; set; }
    public float AngvelImpulseZ { get; set; }
    public float ImpulseX { get; set; }
    public float ImpulseY { get; set; }
    public float ImpulseZ { get; set; }
    public float AngImpulseX { get; set; }
    public float AngImpulseY { get; set; }
    public float AngImpulseZ { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        Logger.Debug("ImpulseEffect: caster={0}, target={1}, impulse=({2},{3},{4}), velImpulse=({5},{6},{7})",
            caster?.ObjId, target?.ObjId, ImpulseX, ImpulseY, ImpulseZ, VelImpulseX, VelImpulseY, VelImpulseZ);

        var impulseTarget = ResolveImpulseTarget(caster, target);
        if (impulseTarget == null || impulseTarget is Unit { IsDead: true })
            return;

        if (target?.Buffs.HasEffectsMatchingCondition(e => e.Template.KnockbackImmune || e.Template.NonPushable) == true)
            return;

        if (target is Npc npcTarget && npcTarget.Template.NonPushableByActor)
            return;

        var impulse = new Vector3(ImpulseX + VelImpulseX, ImpulseY + VelImpulseY, ImpulseZ + VelImpulseZ);

        if (impulse.LengthSquared() < 0.01f)
            return;

        if (caster == null || target == null)
            return;

        impulseTarget.BroadcastPacket(new SCUnitImpulsePacket(impulseTarget.ObjId,
            VelImpulseX, VelImpulseY, VelImpulseZ,
            AngvelImpulseX, AngvelImpulseY, AngvelImpulseZ,
            ImpulseX, ImpulseY, ImpulseZ,
            AngImpulseX, AngImpulseY, AngImpulseZ), impulseTarget is Character);

        var casterPos = caster.Transform.World.Position;
        var targetPos = target.Transform.World.Position;
        var displacement = LocalImpulseToWorldDisplacement(impulse, casterPos, targetPos);

        var endPos = targetPos + displacement;

        if (target is Npc npc)
        {
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

    private static BaseUnit ResolveImpulseTarget(BaseUnit caster, BaseUnit target)
    {
        if (target is Slave)
            return target;

        if (caster is Slave)
            return caster;

        if (target == caster && caster is Character character)
        {
            var slave = GetCharacterSlave(character);
            if (slave != null)
                return slave;
        }

        return target;
    }

    private static Slave GetCharacterSlave(Character character)
    {
        if (character.ParentWorld == null)
            return null;

        return character.ParentWorld.SlaveManager.GetIsMounted(character.ObjId, out _)
               ?? character.ParentWorld.SlaveManager.GetActiveSlaveByOwnerObjId(character.ObjId);
    }

    /// <summary>
    /// Converts a caster-target local impulse (X=right, Y=forward, Z=up, units = 1/1000 m)
    /// into a world-space displacement vector. When the caster-to-target direction is
    /// degenerate, the impulse is returned unchanged in world space.
    /// </summary>
    internal static Vector3 LocalImpulseToWorldDisplacement(
        Vector3 localImpulse, Vector3 casterPos, Vector3 targetPos)
    {
        var displacement = localImpulse / 1000f;

        var dir = targetPos - casterPos;
        dir.Z = 0;
        if (dir.LengthSquared() <= 0.01f)
            return displacement;

        dir = Vector3.Normalize(dir);
        var right = new Vector3(-dir.Y, dir.X, 0);
        return right * displacement.X + dir * displacement.Y + Vector3.UnitZ * displacement.Z;
    }
}
