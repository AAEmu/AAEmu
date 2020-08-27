using System;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.DoodadObj;
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
        public float BottomZ { get; set; }
        public float RotationX { get; set; }
        public float RotationY { get; set; }
        public float RotationZ { get; set; } // yaw
        public float RotationW { get; set; } // scalar
        public float Scale { get; set; }
        public Gimmick Last { get; set; }

        public override Gimmick Spawn(uint objId) //TODO clean up each Gimmick uses the same call
        {
            //var gimmick = GimmickManager.Instance.Create(objId, UnitId, null);
            var gimmick = GimmickManager.Instance.Create(objId, UnitId, this);
            if (gimmick == null)
            {
                _log.Warn("Gimmick {0}, from spawn not exist at db", UnitId);
                return null;
            }

            gimmick.Spawner = this;
            gimmick.Position = Position.Clone();
            gimmick.EntityGuid = EntityGuid;
            if (Scale > 0)
            {
                gimmick.SetScale(Scale);
            }

            if (gimmick.Position == null)
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

        public void DecreaseCount(Doodad doodad)
        {
            if (RespawnTime > 0)
            {
                doodad.Respawn = DateTime.Now.AddSeconds(RespawnTime);
                SpawnManager.Instance.AddRespawn(doodad);
            }
            else
            {
                Last = null;
            }

            doodad.Delete();
        }
    }
}
