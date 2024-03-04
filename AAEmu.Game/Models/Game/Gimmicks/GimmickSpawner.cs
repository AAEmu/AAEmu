﻿using System;
using System.Numerics;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;

using NLog;

namespace AAEmu.Game.Models.Game.Gimmicks;

public enum OffsetCoordiateType
{
    Unk0 = 0,
    Unk1 = 1,
    Unk2 = 2,
    Unk3 = 3
}
public enum VelocityCoordiateType
{
    Unk0 = 0,
    Unk1 = 1,
    Unk2 = 2
}
public enum AngVelCoordiateType
{
    Unk0 = 0,
    Unk1 = 1,
    Unk2 = 2
}

public class GimmickSpawner : Spawner<Gimmick>
{
    protected static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

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
    public Gimmick Last { get; set; }
    public uint Count { get; set; }
    public bool OffsetFromSource { get; set; }
    public OffsetCoordiateType OffsetCoordiateId { get; set; }
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    public float OffsetZ { get; set; }
    public VelocityCoordiateType VelocityCoordiateId { get; set; }
    public float VelocityX { get; set; }
    public float VelocityY { get; set; }
    public float VelocityZ { get; set; }
    public AngVelCoordiateType AngVelCoordiateId { get; set; }
    public float AngVelX { get; set; }
    public float AngVelY { get; set; }
    public float AngVelZ { get; set; }

    public GimmickSpawner(SpawnGimmickEffect sgEffect, BaseUnit caster)
    {
        GimmickId = sgEffect.GimmickId;
        OffsetFromSource = sgEffect.OffsetFromSource;
        OffsetCoordiateId = (OffsetCoordiateType)sgEffect.OffsetCoordiateId;
        OffsetX = sgEffect.OffsetX;
        OffsetY = sgEffect.OffsetY;
        OffsetZ = sgEffect.OffsetZ;
        Scale = sgEffect.Scale;
        VelocityCoordiateId = (VelocityCoordiateType)sgEffect.VelocityCoordiateId;
        VelocityX = sgEffect.VelocityX;
        VelocityY = sgEffect.VelocityY;
        VelocityZ = sgEffect.VelocityZ;
        AngVelCoordiateId = (AngVelCoordiateType)sgEffect.AngVelCoordiateId;
        AngVelX = sgEffect.AngVelX;
        AngVelY = sgEffect.AngVelY;
        AngVelZ = sgEffect.AngVelZ;
        Count = 1;

        var gimmick = GimmickManager.Instance.Create(GimmickId);

        gimmick.Spawner = this;
        gimmick.Spawner.RespawnTime = 0; // don't respawn
        gimmick.Transform.ApplyWorldSpawnPosition(caster.Transform.CloneAsSpawnPosition());
        switch (OffsetCoordiateId)
        {
            case OffsetCoordiateType.Unk0:
                var (newX0, newY0, newZ0) = PositionAndRotation.AddDistanceToFront(1, 1, gimmick.Transform.World.Position, gimmick.Transform.World.Position);
                gimmick.Transform.World.Position = new Vector3(newX0, newY0, newZ0);
                break;
            case OffsetCoordiateType.Unk1:
                break;
            case OffsetCoordiateType.Unk2:
                //gimmick.Transform.World.Position = new Vector3(
                //    gimmick.Transform.World.Position.X - OffsetX,
                //    gimmick.Transform.World.Position.Y - OffsetY,
                //    gimmick.Transform.World.Position.Z + OffsetZ
                //);

                var (newX, newY, newZ) = PositionAndRotation.AddDistanceToFront(1, 1, gimmick.Transform.World.Position, gimmick.Transform.World.Position);
                gimmick.Transform.World.Position = new Vector3(newX, newY, newZ + OffsetZ);
                break;
            case OffsetCoordiateType.Unk3:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        gimmick.EntityGuid = 0;
        gimmick.SpawnerUnitId = caster.ObjId;
        gimmick.GrasperUnitId = 0;

        gimmick.SetScale(Scale);
        gimmick.Spawn(); // добавляем в мир

        if (caster is Npc npc)
        {
            npc.Gimmick = gimmick;
        }
    }

    public GimmickSpawner()
    {
        Count = 1;
    }

    public override Gimmick Spawn(uint objId)
    {
        var gimmick = GimmickManager.Instance.Create(objId, UnitId, this);
        if (gimmick == null)
        {
            Logger.Warn("Gimmick {0}, from spawn not exist at db", UnitId);
            return null;
        }

        Last = gimmick;
        return gimmick;
    }

    public override void Despawn(Gimmick gimmick)
    {
        gimmick.Delete();
        if (gimmick.Respawn == DateTime.MinValue)
        {
            ObjectIdManager.Instance.ReleaseId(gimmick.ObjId);
        }

        Last = null;
    }

    public void DecreaseCount(Gimmick gimmick)
    {
        if (RespawnTime > 0)
        {
            gimmick.Respawn = DateTime.UtcNow.AddSeconds(RespawnTime);
            SpawnManager.Instance.AddRespawn(gimmick);
        }
        else
        {
            Last = null;
        }

        gimmick.Delete();
    }
}
