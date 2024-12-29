using System;
using System.Numerics;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Skills;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Gimmicks;

public class Gimmick : Unit
{
    public override UnitTypeFlag TypeFlag { get; } = UnitTypeFlag.None; // TODO для Gimmick не понятно что выбрать
    public ushort GimmickId { get; set; }
    public long EntityGuid { get; set; } // TODO это не Guid в GameObject
    public GimmickTemplate Template { get; set; }
    public uint SpawnerUnitId { get; set; }
    public uint GrasperUnitId { get; set; }
    public string ModelPath { get; set; }
    // public Quaternion Rot { get; set; } // углы должны быть в радианах
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
    public TimeSpan TotalLifeTime { get; set; } = TimeSpan.Zero;
    private TimeSpan LastLifeTime { get; set; } = TimeSpan.Zero;
    private Vector3 LastPos { get; set; } = Vector3.Zero;
    private Vector3 LastRot { get; set; } = Vector3.Zero;
    private bool SkillStarted { get; set; } = false;
    private readonly object _skillStartedLock = new(); 
    public GimmickMovementHandler MovementHandler { get; set; }

    public void SetScale(float scale)
    {
        Scale = scale;
    }

    public PacketStream Write(PacketStream stream)
    {
        // stream.Write((uint)GimmickId);     // GimmickId
        stream.Write(ObjId);            // same as ObjId in GameObject
        stream.Write(TemplateId);       // GimmickTemplateId
        stream.Write(EntityGuid);       // entityGUID = 0x4227234CE506AFDB box
        stream.Write((uint)Faction.Id);       // Faction
        stream.Write(SpawnerUnitId);    // spawnerUnitId
        stream.Write(GrasperUnitId);    // grasperUnitId
        stream.Write(Transform.ZoneId);
        stream.Write(Template?.ModelPath ?? "", true);
        //stream.Write("", true); // ModelPath

        stream.Write(Helpers.ConvertLongX(Transform.World.Position.X)); // WorldPosition qx,qx,fz
        stream.Write(Helpers.ConvertLongY(Transform.World.Position.Y));
        stream.Write(Transform.World.Position.Z);

        var rotation = Transform.World.ToQuaternion();
        stream.Write(rotation.X); // Quaternion Rotation
        stream.Write(rotation.Y);
        stream.Write(rotation.Z);
        stream.Write(rotation.W);

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

    /// <summary>
    /// Used by NPC AI to move its own spawned gimmicks
    /// </summary>
    /// <param name="other"></param>
    /// <param name="distance"></param>
    /// <param name="distanceZ"></param>
    public void MoveTowards(Vector3 other, float distance, float distanceZ)
    {
        var oldPosition = Transform.Local.ClonePosition();
        var targetDist = MathUtil.CalculateDistance(Transform.Local.Position, other, true);
        var travelDist = Math.Min(targetDist, distance);
        var (newX, newY, newZ) = World.Transform.PositionAndRotation.AddDistanceToFront(travelDist, targetDist, Transform.Local.Position, other);
        Transform.Local.SetPosition(newX, newY, newZ);
        Time = (uint)(DateTime.UtcNow - DateTime.UtcNow.Date).TotalMilliseconds;

        var q = RotateBarrel(Pitch, Yaw, Roll);
        Transform.Local.ApplyFromQuaternion(q);
        Vel = new Vector3(0, 0, -distanceZ);
        AngVel = new Vector3(0f, 0f, 0f);

        if (CheckMovedPosition(oldPosition))
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
        DoGimmickSkill(Template?.SkillId ?? 0);
    }
    
    public override void AddVisibleObject(Character character)
    {
        character.SendPacket(new SCGimmicksCreatedPacket([this]));
        character.SendPacket(new SCGimmickJointsBrokenPacket([]));
        base.AddVisibleObject(character);
    }

    public override void RemoveVisibleObject(Character character)
    {
        base.RemoveVisibleObject(character);
        character.SendPacket(new SCGimmicksRemovedPacket([ObjId]));
    }

    /// <summary>
    /// Helper function to execute a Gimmick's skill
    /// </summary>
    /// <param name="skillId"></param>
    private void DoGimmickSkill(uint skillId)
    {
        if (skillId <= 0)
            return;

        lock (_skillStartedLock)
        {
            if (SkillStarted)
                return;
            SkillStarted = true;
        }

        var skillTemplate = SkillManager.Instance.GetSkillTemplate(skillId);
        var caster = WorldManager.Instance.GetUnit(SpawnerUnitId);
        var skillCaster = new SkillDoodad(ObjId);
        var skillCastTarget = new SkillCastPositionTarget
        {
            PosX = Transform.World.Position.X, PosY = Transform.World.Position.Y, PosZ = Transform.World.Position.Z,
            PosRot = 0f,
            ObjId = 0,
            ObjId1 = 0,
            ObjId2 = 0
        };
        var skillObject = new SkillObject();

        var useSkill = new Skill(skillTemplate);
        TaskManager.Instance.Schedule(new UseSkillTask(useSkill, caster, skillCaster, this, skillCastTarget, skillObject), TimeSpan.FromMilliseconds(0));
        // var skill = new Skill(SkillManager.Instance.GetSkillTemplate(skillId));
        // var skillResult = skill.Use(caster, skillCaster, skillCastTarget, null, true, out _);
        
        BroadcastPacket(new SCChatMessagePacket(ChatType.System, $"Gimmick {ObjId} used skill {skillId}"), false);
    }
    
    public void GimmickTick(TimeSpan delta)
    {
        LastLifeTime = TotalLifeTime;
        TotalLifeTime += delta;
        if (TimeLeft > 0)
            return;
        
        MovementHandler?.Tick(delta);
        
        // Handle Delayed Skills
        if ((Template?.SkillDelay > 0) && (!SkillStarted) && (LastLifeTime.TotalMilliseconds < Template.SkillDelay) && (TotalLifeTime.TotalMilliseconds >= Template.SkillDelay))
        {
            DoGimmickSkill(Template.SkillId);
        }
        
        // TODO: Skill on collision (requires physics engine rewrite)

        var deltaTime = (float)delta.TotalSeconds;
        var deltaPosition = Transform.World.Position - LastPos;
        Vel = deltaPosition * deltaTime;
        AngVel = new Vector3(0f, 0f, 0f);
        
        // Time += (uint)delta.Milliseconds;
        Time = (uint)(DateTime.UtcNow - DateTime.UtcNow.Date).TotalMilliseconds;

        BroadcastPacket(new SCGimmickMovementPacket(this), false);
        
        LastPos = Transform.World.Position;
        LastRot = Transform.World.Rotation;

        MovementHandler?.AfterMove(delta, deltaPosition); 

        // Check LifeTime and apply despawn time if needed
        if ((Template?.LifeTime > 0) && (Despawn <= DateTime.MinValue) && (TotalLifeTime.TotalMilliseconds >= Template.LifeTime))// && (LastLifeTime.TotalMilliseconds < Template.LifeTime))
        {
            Despawn = DateTime.UtcNow;
            Spawner?.Despawn(this);
        }
    }

    /// <summary>
    /// Used by Elevator code
    /// </summary>
    /// <param name="gimmick"></param>
    /// <param name="position"></param>
    /// <param name="target"></param>
    /// <param name="maxVelocity"></param>
    /// <param name="deltaTime"></param>
    /// <param name="movingDistance"></param>
    /// <param name="velocityZ"></param>
    /// <param name="isMovingDown"></param>
    public void MoveAlongZAxis(Gimmick gimmick, ref Vector3 position, Vector3 target, float maxVelocity, float deltaTime, float movingDistance, ref float velocityZ, ref bool isMovingDown)
    {
        var distance = target - position;
        velocityZ = maxVelocity * Math.Sign(distance.Z);
        movingDistance = velocityZ * deltaTime;

        if (Math.Abs(distance.Z) >= Math.Abs(movingDistance))
        {
            position.Z += movingDistance;
            gimmick.Vel = gimmick.Vel with { Z = velocityZ };
        }
        else
        {
            position.Z = target.Z;
            gimmick.Vel = Vector3.Zero;
        }
    }
}
