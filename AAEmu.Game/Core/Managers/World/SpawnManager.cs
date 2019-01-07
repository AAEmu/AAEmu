using System;
using System.Collections.Generic;
using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;
using NLog;

namespace AAEmu.Game.Core.Managers.World
{
    public class SpawnManager : Singleton<SpawnManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private Dictionary<byte, Dictionary<uint, NpcSpawner>> _npcSpawners;
        private Dictionary<byte, Dictionary<uint, DoodadSpawner>> _doodadSpawners;

        public void Load()
        {
            _npcSpawners = new Dictionary<byte, Dictionary<uint, NpcSpawner>>();
            _doodadSpawners = new Dictionary<byte, Dictionary<uint, DoodadSpawner>>();

            var worlds = WorldManager.Instance.GetWorlds();
            _log.Info("Loading spawns...");
            foreach (var world in worlds)
            {
                var npcSpawners = new Dictionary<uint, NpcSpawner>();
                var doodadSpawners = new Dictionary<uint, DoodadSpawner>();

                var contents = FileManager.GetFileContents($"{FileManager.AppPath}Data/Worlds/{world.Name}/npc_spawns.json");
                if (string.IsNullOrWhiteSpace(contents))
                    _log.Warn($"File {FileManager.AppPath}Data/Worlds/{world.Name}/npc_spawns.json doesn't exists or is empty.");
                else
                {
                    if (JsonHelper.TryDeserializeObject(contents, out List<NpcSpawner> spawners, out _))
                        foreach (var spawner in spawners)
                        {
                            spawner.Position.ZoneId = WorldManager.Instance.GetZoneId(world.Id, spawner.Position.X, spawner.Position.Y);
                            npcSpawners.Add(spawner.Id, spawner);
                        }
                    else
                        throw new Exception(
                            $"SpawnManager: Parse {FileManager.AppPath}Data/Worlds/{world.Name}/npc_spawns.json file");
                }

                contents = FileManager.GetFileContents($"{FileManager.AppPath}Data/Worlds/{world.Name}/doodad_spawns.json");
                if (string.IsNullOrWhiteSpace(contents))
                    _log.Warn($"File {FileManager.AppPath}Data/Worlds/{world.Name}/doodad_spawns.json doesn't exists or is empty.");
                else
                {
                    if (JsonHelper.TryDeserializeObject(contents, out List<DoodadSpawner> spawners, out _))
                        foreach (var spawner in spawners)
                        {
                            spawner.Position.ZoneId = WorldManager.Instance.GetZoneId(world.Id, spawner.Position.X, spawner.Position.Y);
                            doodadSpawners.Add(spawner.Id, spawner);
                        }
                    else
                        throw new Exception(
                            $"SpawnManager: Parse {FileManager.AppPath}Data/Worlds/{world.Name}/doodad_spawns.json file");
                }

                _npcSpawners.Add((byte) world.Id, npcSpawners);
                _doodadSpawners.Add((byte) world.Id, doodadSpawners);
            }
        }

        public void Spawn() // TODO only world now...
        {
            foreach (var spawner in _npcSpawners[1].Values)
                spawner.Spawn(0);
            foreach (var spawner in _doodadSpawners[1].Values)
                spawner.Spawn(0);
        }
    }
}