using System.Numerics;

using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class PhysicalExplosionEffect : EffectTemplate
{
    public float Radius { get; set; }
    public float HoleSize { get; set; }
    public float Pressure { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        Logger.Debug("PhysicalExplosionEffect: caster={0}, target={1}, radius={2}, pressure={3}",
            caster?.ObjId, target?.ObjId, Radius, Pressure);

        if (caster == null || target == null)
            return;

        if (target.Buffs.HasEffectsMatchingCondition(e => e.Template.KnockbackImmune || e.Template.NonPushable))
            return;

        if (target is Npc npcTarget && npcTarget.Template.NonPushableByActor)
            return;

        var casterPos = caster.Transform.World.Position;
        var targetPos = target.Transform.World.Position;

        var displacement = ComputeKnockDisplacement(casterPos, targetPos, Radius, HoleSize, Pressure);
        if (!displacement.HasValue)
            return;

        var endPos = targetPos + displacement.Value;

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

    /// <summary>
    /// Computes the knock displacement vector from an explosion. Returns null when the
    /// target is outside the explosion radius, inside the dead-zone hole, or when the
    /// resulting knock distance is below a perceptible threshold (0.5m).
    /// </summary>
    internal static Vector3? ComputeKnockDisplacement(
        Vector3 casterPos, Vector3 targetPos, float radius, float holeSize, float pressure)
    {
        var direction = targetPos - casterPos;
        direction.Z = 0;
        var dist = direction.Length();

        if (dist > radius || dist < holeSize)
            return null;

        if (direction.LengthSquared() < 0.01f)
            direction = new Vector3(1, 0, 0);
        direction = Vector3.Normalize(direction);

        var falloff = radius > 0f ? 1f - (dist / radius) : 0f;
        var knockDist = (pressure / 1000f) * falloff;
        if (knockDist < 0.5f)
            return null;

        return direction * knockDist;
    }
}
