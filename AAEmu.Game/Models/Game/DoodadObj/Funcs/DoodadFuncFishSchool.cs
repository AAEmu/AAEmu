using System.Collections.Generic;

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncFishSchool : DoodadPhaseFuncTemplate
    {
        public uint NpcSpawnerId { get; set; }

        public override bool Use(Unit caster, Doodad owner)
        {
            _log.Debug("DoodadFuncFishSchool");

            var npcSpawnerNpc = NpcGameData.Instance.GetNpcSpawnerNpc(NpcSpawnerId);
            var unitId = npcSpawnerNpc.MemberId;

            var spawner = SpawnManager.Instance.GetNpcSpawner(NpcSpawnerId, (byte)caster.Transform.WorldId);
            if (spawner.Count == 0)
            {
                spawner.Add(new NpcSpawner());
                //var npcSpawnersIds = new List<uint> { NpcSpawnerId };//NpcGameData.Instance.GetSpawnerIds(unitId);
                spawner[0].UnitId = unitId;
                spawner[0].Id = NpcSpawnerId;
                spawner[0].NpcSpawnerIds = new List<uint> { NpcSpawnerId };
                spawner[0].Template = NpcGameData.Instance.GetNpcSpawnerTemplate(NpcSpawnerId);
                if (spawner[0].Template == null)
                {
                    return false;
                }

                spawner[0].Template.Npcs = new List<NpcSpawnerNpc>();
                var nsn = NpcGameData.Instance.GetNpcSpawnerNpc(NpcSpawnerId);
                if (nsn == null)
                {
                    return false;
                }

                spawner[0].Template.Npcs.Add(nsn);
                if (spawner[0].Template.Npcs == null)
                {
                    return false;
                }

                spawner[0].Template.Npcs[0].MemberId = unitId;
                spawner[0].Template.Npcs[0].UnitId = unitId;

            }
            var spawnPos = owner.Transform.Clone();
            //spawnPos.World.AddDistanceToFront(3f);
            //spawnPos.World.SetHeight(WorldManager.Instance.GetHeight(spawnPos));
            spawner[0].Position = spawnPos.CloneAsSpawnPosition();
            spawner[0].Spawn(0);

            return false;
        }
    }
}
