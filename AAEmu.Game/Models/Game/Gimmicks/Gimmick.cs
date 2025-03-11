using System;
using System.Numerics;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Gimmicks;

public class Gimmick : Unit
{
    public override UnitTypeFlag TypeFlag { get; } = UnitTypeFlag.None; // TODO для Gimmick не понятно что выбрать
    public long EntityGuid { get; set; } // TODO это не Guid в GameObject
    public GimmickTemplate Template { get; set; }
    public uint SpawnerUnitId { get; set; }
    public uint GrasperUnitId { get; set; }
    public string ModelPath { get; set; }
    public Quaternion Rot { get; set; } // углы должны быть в радианах
    public Vector3 Vel { get; set; }
    public Vector3 AngVel { get; set; }
    public Vector3 Target { get; set; } = Vector3.Zero;
    public float ScaleVel { get; set; }
    public uint Time { get; set; }
    public GimmickSpawner Spawner { get; set; }
    /// <summary>
    /// MoveZ
    /// </summary>
    public bool moveDown { get; set; } = false;
    public DateTime WaitTime { get; set; }
    public uint TimeLeft => WaitTime > DateTime.UtcNow ? (uint)(WaitTime - DateTime.UtcNow).TotalMilliseconds : 0;

    public override void AddVisibleObject(Character character)
    {
        character.SendPacket(new SCGimmicksCreatedPacket(new[] { this }));
        var temp = Array.Empty<Gimmick>();
        character.SendPacket(new SCGimmickJointsBrokenPacket(temp));
        base.AddVisibleObject(character);
    }

    public override void RemoveVisibleObject(Character character)
    {
        base.RemoveVisibleObject(character);
        character.SendPacket(new SCGimmicksRemovedPacket([this.ObjId]));
    }

    public void SetScale(float scale)
    {
        Scale = scale;
    }

    public PacketStream Write(PacketStream stream)
    {
        stream.Write(ObjId);            // same as ObjId in GameObject
        stream.Write(TemplateId);       // TemplateId aka GimmickId
        stream.Write(EntityGuid);       // entityGUID = 0x4227234CE506AFDB box
        stream.Write((uint)Faction.Id);       // Faction
        stream.Write(SpawnerUnitId);    // spawnerUnitId
        stream.Write(GrasperUnitId);    // grasperUnitId
        stream.Write(Transform.ZoneId);
        stream.Write((short)0);         // ModelPath

        stream.Write(Helpers.ConvertLongX(Transform.World.Position.X)); // WorldPosition qx,qx,fz
        stream.Write(Helpers.ConvertLongY(Transform.World.Position.Y));
        stream.Write(Transform.World.Position.Z);

        stream.Write(Rot.X); // Quaternion Rotation
        stream.Write(Rot.Y);
        stream.Write(Rot.Z);
        stream.Write(Rot.W);

        stream.Write(Scale);

        stream.Write(Vel.X);    // vector3 vel
        stream.Write(Vel.Y);
        stream.Write(Vel.Z);

        stream.Write(AngVel.X); // vector3 angVel
        stream.Write(AngVel.Y);
        stream.Write(AngVel.Z);

        stream.Write(ScaleVel);

        return stream;
    }

    public void MoveTowards(Vector3 other, float distance, float distanceZ)
    {
        var oldPosition = Transform.Local.ClonePosition();
        var targetDist = MathUtil.CalculateDistance(Transform.Local.Position, other, true);
        var travelDist = Math.Min(targetDist, distance);
        var (newX, newY, newZ) = World.Transform.PositionAndRotation.AddDistanceToFront(travelDist, targetDist, Transform.Local.Position, other);
        Transform.Local.SetPosition(newX, newY, newZ);
        Time = (uint)(DateTime.UtcNow - DateTime.UtcNow.Date).TotalMilliseconds;

        Rot = RotateBarrel(Pitch, Yaw, Roll);
        Vel = new Vector3(0, 0, -distanceZ);
        AngVel = new Vector3(0f, 0f, 0f);

        CheckMovedPosition(oldPosition);
        BroadcastPacket(new SCGimmickMovementPacket(this), false);
    }

    private float Pitch;
    private float Yaw;
    private float Roll;
    private Quaternion RotateBarrel(float xRotation, float yRotation, float zRotation)
    {
        Pitch = (Pitch + Spawner.VelocityX) % 360;
        Yaw = (Yaw + Spawner.VelocityY) % 360;
        Roll = (Roll + Spawner.VelocityZ) % 360;

        // Создаем новый Quaternion с заданными значениями вращения
        return Quaternion.CreateFromYawPitchRoll(xRotation.DegToRad(), yRotation.DegToRad(), zRotation.DegToRad());
    }

    public void StopMovement()
    {
        if (CurrentTarget == null)
            return;
        var caster = WorldManager.Instance.GetNpc(SpawnerUnitId);

        var skillCaster = SkillCaster.GetByType(SkillCasterType.Gimmick);
        skillCaster.ObjId = this.ObjId;

        var skillCastTarget = SkillCastTarget.GetByType(SkillCastTargetType.Position);
        if (skillCastTarget is not SkillCastPositionTarget sct)
            return;
        sct.PosX = this.Transform.World.Position.X;
        sct.PosY = this.Transform.World.Position.Y;
        sct.PosZ = this.Transform.World.Position.Z;
        sct.PosRot = 0f;
        sct.ObjId = 0;
        sct.ObjId1 = 0;
        sct.ObjId2 = 0;

        var skill = new Skill(SkillManager.Instance.GetSkillTemplate(Template.SkillId));
        var skillResult = skill.Use(caster, skillCaster, skillCastTarget, null, false, out _);

        caster.Gimmick.Spawner.Despawn(caster.Gimmick);
        caster.Gimmick = null;
        CurrentTarget = null;
    }
}
