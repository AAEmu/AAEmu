using System;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.World;
using NLog;

namespace AAEmu.Game.Models.Game.DoodadObj
{
    public class DoodadSpawner : Spawner<Doodad>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        public float Scale { get; set; }
        public Doodad Last { get; set; }

        public override Doodad Spawn(uint objId, ulong itemId, uint charId) //Mostly used for player created spawns
        {

            var character = WorldManager.Instance.GetCharacterByObjId(charId);
            var doodad = DoodadManager.Instance.Create(objId, UnitId, character);

            if (doodad == null)
            {
                _log.Warn("Doodad {0}, from spawn not exist at db", UnitId);
                return null;
            }

            doodad.Spawner = this;
            doodad.Transform.ApplyWorldSpawnPosition(Position);
            doodad.QuestGlow = 0u; // TODO: make this OOP
            doodad.ItemId = itemId;

            // TODO for test
            doodad.PlantTime = DateTime.UtcNow;
            //if (doodad.GrowthTime.Millisecond <= 0)
            //{
            //    //doodad.GrowthTime = DateTime.UtcNow.AddMilliseconds(template.MinTime);
            //    doodad.GrowthTime = DateTime.UtcNow.AddMilliseconds(10000);
            //}

            if (Scale > 0)
                doodad.SetScale(Scale);

            if (doodad.Transform == null)
            {
                _log.Error("Can't spawn doodad {1} from spawn {0}", Id, UnitId);
                return null;
            }

            doodad.Spawn();
            Last = doodad;
            return doodad;
        }

        public override Doodad Spawn(uint objId) // TODO: clean up each doodad uses the same call
        {  
            var doodad = DoodadManager.Instance.Create(objId, UnitId, null);
            if (doodad == null)
            {
                _log.Warn("Doodad {0}, from spawn not exist at db", UnitId);
                return null;
            }
            
            doodad.Spawner = this;
            doodad.Transform.ApplyWorldSpawnPosition(Position);
            // TODO for test
            doodad.PlantTime = DateTime.UtcNow;
            //if (doodad.GrowthTime.Millisecond <= 0)
            //{
            //    doodad.GrowthTime = DateTime.UtcNow.AddMilliseconds(doodad.Template.MinTime);
            //doodad.GrowthTime = DateTime.UtcNow.AddMilliseconds(10000);
            //}
            if (Scale > 0)
                doodad.SetScale(Scale);
            if (doodad.Transform == null)
            {
                _log.Error("Can't spawn doodad {1} from spawn {0}", Id, UnitId);
                return null;
            }

            doodad.Spawn();
            Last = doodad;
            return doodad;
        }

        public override void Despawn(Doodad doodad)
        {
            doodad.Delete();
            if (doodad.Respawn == DateTime.MinValue)
                ObjectIdManager.Instance.ReleaseId(doodad.ObjId);
            Last = null;
        }

        public void DecreaseCount(Doodad doodad)
        {
            if (RespawnTime > 0)
            {
                doodad.Respawn = DateTime.UtcNow.AddSeconds(RespawnTime);
                SpawnManager.Instance.AddRespawn(doodad);
            }
            else
                Last = null;

            doodad.Delete();
        }
    }
}
