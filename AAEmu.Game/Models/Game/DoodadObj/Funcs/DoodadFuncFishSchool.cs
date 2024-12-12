using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncFishSchool : DoodadPhaseFuncTemplate
{
    public uint NpcSpawnerId { get; set; }

    public override bool Use(BaseUnit caster, Doodad owner)
    {
        Logger.Debug($"DoodadFuncFishSchool NpcSpawnerId={NpcSpawnerId}");

        var npcSpawnerNpc = NpcGameData.Instance.GetNpcSpawnerNpc(NpcSpawnerId);
        var unitId = npcSpawnerNpc.MemberId;

        var spawner = SpawnManager.Instance.GetNpcSpawner(NpcSpawnerId, (byte)caster.Transform.WorldId);
        if (spawner.Count == 0)
        {
            spawner.Add(new NpcSpawner());
            //var npcSpawnersIds = new List<uint> { NpcSpawnerId };//NpcGameData.Instance.GetSpawnerIds(unitId);
            spawner[0].UnitId = unitId;
            spawner[0].Id = NpcSpawnerId;
            spawner[0].NpcSpawnerIds = [NpcSpawnerId];
            spawner[0].Template = NpcGameData.Instance.GetNpcSpawnerTemplate(NpcSpawnerId);
            if (spawner[0].Template == null) { return false; }

            if (spawner[0].Template.Npcs.Count == 0)
            {
                spawner[0].Template.Npcs = [];
                var nsn = NpcGameData.Instance.GetNpcSpawnerNpc(NpcSpawnerId);
                if (nsn == null) { return false; }
                spawner[0].Template.Npcs.Add(nsn);
            }
            if (spawner[0].Template.Npcs == null) { return false; }

            spawner[0].Template.Npcs[0].MemberId = unitId;
            spawner[0].Template.Npcs[0].UnitId = unitId;

        }
        using var spawnPos = owner.Transform.Clone();
        //spawnPos.World.AddDistanceToFront(3f);
        //spawnPos.World.SetHeight(WorldManager.Instance.GetHeight(spawnPos));
        spawner[0].Position = spawnPos.CloneAsSpawnPosition();
        var npc = spawner[0].Spawn(0);
        npc.Spawner.RespawnTime = 0; // запретим респавн

        return false;
    }
}
