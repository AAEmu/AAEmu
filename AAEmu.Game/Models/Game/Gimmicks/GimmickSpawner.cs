using System;
using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.World;

using NLog;

namespace AAEmu.Game.Models.Game.Gimmicks
{
    public class GimmickSpawner : Spawner<Gimmick>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
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
        private List<Gimmick> _spawned;
        public uint Count { get; set; }

        public GimmickSpawner()
        {
            _spawned = new List<Gimmick>();
            Count = 1;
        }

        public override Gimmick Spawn(uint objId)
        {
            var gimmick = GimmickManager.Instance.Create(objId, UnitId, this);
            if (gimmick == null)
            {
                _log.Warn("Gimmick {0}, from spawn not exist at db", UnitId);
                return null;
            }

            gimmick.Spawner = this;
            gimmick.Transform.ApplyWorldSpawnPosition(Position);
            gimmick.EntityGuid = EntityGuid;
            if (Scale > 0)
            {
                gimmick.SetScale(Scale);
            }

            if (gimmick.Transform.World.IsOrigin())
            {
                _log.Error("Can't spawn gimmick {1} from spawn {0}", Id, UnitId);
                return null;
            }

            gimmick.Spawn();
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
}
