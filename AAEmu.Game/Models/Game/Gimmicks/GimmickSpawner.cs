using System.Numerics;
using System.Text.Json.Serialization;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;

using NLog;

#pragma warning disable IDE0079 // Remove unnecessary suppression

namespace AAEmu.Game.Models.Game.Gimmicks;

public class GimmickSpawner : Spawner<Gimmick>
{
    [JsonIgnore]
    protected static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    [JsonIgnore]
    public WorldInstance ParentWorld { get; set; }

    public uint GimmickId { get; set; } // here we mean TemplateId
    public long EntityGuid { get; set; }
    public float WaitTime { get; set; }
    public float TopZ { get; set; }
    public float MiddleZ { get; set; }
    public float BottomZ { get; set; }
    public float RotationX { get; set; }
    public float RotationY { get; set; }
    public float RotationZ { get; set; }
    public float RotationW { get; set; }
    //public Quaternion Rot { get; set; }
    public float Scale { get; set; }
    [JsonIgnore]
    public Gimmick Last { get; set; }
    public uint Count { get; set; }
    public bool OffsetFromSource { get; set; }
    public OffsetCoordinateType OffsetCoordinateId { get; set; }
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    public float OffsetZ { get; set; }
    public VelocityCoordinateType VelocityCoordinateId { get; set; }
    public float VelocityX { get; set; }
    public float VelocityY { get; set; }
    public float VelocityZ { get; set; }
    public AngVelCoordinateType AngVelCoordinateId { get; set; }
    public float AngVelX { get; set; }
    public float AngVelY { get; set; }
    public float AngVelZ { get; set; }

    public GimmickSpawner()
    {
        // DefaultConstructor for JSON reading
    }
    public GimmickSpawner(WorldInstance parentWorld, SpawnGimmickEffect sgEffect, BaseUnit caster)
    {
        ParentWorld = parentWorld;
        GimmickId = sgEffect.GimmickId;
        OffsetFromSource = sgEffect.OffsetFromSource;
        OffsetCoordinateId = (OffsetCoordinateType)sgEffect.OffsetCoordinateId;
        OffsetX = sgEffect.OffsetX;
        OffsetY = sgEffect.OffsetY;
        OffsetZ = sgEffect.OffsetZ;
        Scale = sgEffect.Scale;
        VelocityCoordinateId = (VelocityCoordinateType)sgEffect.VelocityCoordinateId;
        VelocityX = sgEffect.VelocityX;
        VelocityY = sgEffect.VelocityY;
        VelocityZ = sgEffect.VelocityZ;
        AngVelCoordinateId = (AngVelCoordinateType)sgEffect.AngVelCoordinateId;
        AngVelX = sgEffect.AngVelX;
        AngVelY = sgEffect.AngVelY;
        AngVelZ = sgEffect.AngVelZ;
        Count = 1;

        var gimmick = ParentWorld.GimmickManager.Create(GimmickId);
        gimmick.Spawner = this;
        gimmick.Spawner.RespawnTime = 0; // don't respawn
        gimmick.Transform = caster.Transform.CloneDetached(gimmick);
        gimmick.EntityGuid = 0;
        gimmick.SpawnerUnitId = caster.ObjId;
        gimmick.GrasperUnitId = 0;
        switch (OffsetCoordinateId)
        {
            case OffsetCoordinateType.Unk0:
                var (newX0, newY0, newZ0) = PositionAndRotation.AddDistanceToFront(1, 1, gimmick.Transform.World.Position, gimmick.Transform.World.Position);
                gimmick.Transform.World.Position = new Vector3(newX0, newY0, newZ0);
                break;
            case OffsetCoordinateType.Unk1:
                break;
            case OffsetCoordinateType.Unk2:
                gimmick.Transform.Local.AddDistance(OffsetX, OffsetY, OffsetZ);
                //var (newX, newY, newZ) = PositionAndRotation.AddDistanceToFront(1, 1, gimmick.Transform.World.Position, gimmick.Transform.World.Position);
                //gimmick.Transform.World.Position = new Vector3(newX, newY, newZ + OffsetZ);
                break;
            case OffsetCoordinateType.Unk3:
                break;
            default:
#pragma warning disable CA2208 // Instantiate argument exceptions correctly
                throw new ArgumentOutOfRangeException();
#pragma warning restore CA2208 // Instantiate argument exceptions correctly
        }


        gimmick.SetScale(Scale);
        gimmick.Spawn(); // добавляем в мир
        ParentWorld.GimmickManager.AddActiveGimmick(gimmick);

        if (caster is Npc npc)
        {
            npc.Gimmick = gimmick;
        }
    }

    public GimmickSpawner(WorldInstance parentWorld)
    {
        ParentWorld = parentWorld;
        Count = 1;
    }

    public override Gimmick Spawn(uint objId)
    {
        var gimmick = ParentWorld.GimmickManager.Create(objId, UnitId, this);
        if (gimmick == null)
        {
            Logger.Warn($"Gimmick {UnitId}, from spawn not exist at db");
            return null;
        }

        Last = gimmick;
        return gimmick;
    }

    public override void Despawn(Gimmick gimmick)
    {
        ParentWorld.GimmickManager.RemoveActiveGimmick(gimmick);
        gimmick.Delete();
        if (gimmick.Respawn == DateTime.MinValue)
        {
            if (gimmick.ObjId > 0)
                ObjectIdManager.Instance.ReleaseId(gimmick.ObjId);
            if (gimmick.GimmickId > 0)
                GimmickIdManager.Instance.ReleaseId(gimmick.GimmickId);
        }

        Last = null;
    }

    public void DecreaseCount(Gimmick gimmick)
    {
        if (RespawnTime > 0)
        {
            gimmick.Respawn = DateTime.UtcNow.AddSeconds(RespawnTime);
            ParentWorld.SpawnManager.AddRespawn(gimmick);
        }
        else
        {
            Last = null;
        }

        gimmick.Delete();
    }
}
