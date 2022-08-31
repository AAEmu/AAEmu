using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Json;
using AAEmu.Game.Utils.Converters;
using Newtonsoft.Json;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.Commands
{
    public class SlaveSaveSubCommand : SubCommandBase
    {
        public SlaveSaveSubCommand()
        {
            Title = "[Slave Save]";
            Description = "Save one or all slave positions in the current character world to the world slave spawns file";
            CallPrefix = $"{CommandManager.CommandPrefix}slave save";
            AddParameter(new StringSubCommandParameter("target", "target", true, "all", "id"));
            AddParameter(new NumericSubCommandParameter<uint>("ObjId", "object Id", false));
        }

        public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters)
        {
            if (parameters.TryGetValue("ObjId", out ParameterValue objId))
            {
                SaveById(character, objId);
            } 
            else
            {
                SaveAll(character);
            }
        }

        private void SaveAll(ICharacter character)
        {
            var currentWorld = WorldManager.Instance.GetWorld(((Character)character).Transform.WorldId);

            var allSlaves = WorldManager.Instance.GetAllSlaves();
            var slavesInWorld = WorldManager.Instance.GetAllSlavesFromWorld(currentWorld.Id);

            var slaveSpawnersFromFile = LoadSlavesFromFileByWorld(currentWorld);
            var slaveSpawnersToFile = slaveSpawnersFromFile.ToList();

            foreach(var npc in slavesInWorld.Where(n => n.Spawner is not null))
            {
                switch (npc.Spawner.Id)
                {
                    // spawned into the game manually
                    case 0:
                        {
                            var pos = npc.Transform.World;
                            var newSlaveSpawn = new JsonSlaveSpawns
                            {
                                Id = allSlaves.Last().ObjId++,
                                UnitId = npc.TemplateId,
                                Position = new JsonPosition
                                {
                                    X = pos.Position.X,
                                    Y = pos.Position.Y,
                                    Z = pos.Position.Z,
                                    Roll = pos.Rotation.X.RadToDeg(),
                                    Pitch = pos.Rotation.Y.RadToDeg(),
                                    Yaw = pos.Rotation.Z.RadToDeg()
                                }
                            };

                            slaveSpawnersToFile.Add(newSlaveSpawn);
                            break;
                        }

                    // removed from the game manually
                    case 0xffffffff: //(uint)-1
                        {
                            // Do not add to the output of a manually remote Npc
                            var slaveSpawnsToRemove = new List<JsonSlaveSpawns>();

                            foreach (var slaveSpawnerToFile in slaveSpawnersToFile.Where(n => n.UnitId == npc.TemplateId))
                            {
                                // If the position changed don't mark to be removed
                                if (!npc.Transform.World.Position.Equals(slaveSpawnerToFile.Position.AsVector3()))
                                {
                                    continue;
                                }

                                slaveSpawnsToRemove.Add(slaveSpawnerToFile);
                                break;
                            }

                            foreach (var slaveSpawn in slaveSpawnsToRemove)
                            {
                                slaveSpawnersToFile.Remove(slaveSpawn);
                            }
                            break;
                        }
                }
            }

            var jsonPathOut = Path.Combine(FileManager.AppPath, "Data", "Worlds", currentWorld.Name, "slave_spawns_new.json");
            var json = JsonConvert.SerializeObject(slaveSpawnersToFile.ToArray(), Formatting.Indented, new JsonModelsConverter());
            File.WriteAllText(jsonPathOut, json);
            SendMessage(character, "All slaves have been saved!");
        }

        private void SaveById(ICharacter character, uint objId)
        {
            var spawners = new List<JsonSlaveSpawns>();
            var slave = (Models.Game.Units.Slave)WorldManager.Instance.GetGameObject(objId);
            if (slave is null)
            {
                SendColorMessage(character, Color.Red, "Slave with objId {0} Does not exist |r", objId);
                return;
            }
            
            var world = WorldManager.Instance.GetWorld(slave.Transform.WorldId);
            if (world is null)
            {
                SendColorMessage(character, Color.Red, "Could not find the worldId {0} |r", slave.Transform.WorldId);
                return;
            }

            var spawn = new JsonSlaveSpawns
            {
                Id = slave.ObjId,
                UnitId = slave.TemplateId,
                Position = new JsonPosition
                {
                    X = slave.Transform.Local.Position.X,
                    Y = slave.Transform.Local.Position.Y,
                    Z = slave.Transform.Local.Position.Z,
                    Roll = slave.Transform.Local.Rotation.X.RadToDeg(),
                    Pitch = slave.Transform.Local.Rotation.Y.RadToDeg(),
                    Yaw = slave.Transform.Local.Rotation.Z.RadToDeg()
                }
            };

            Dictionary<uint, JsonSlaveSpawns> spawnersFromFile = new();
            foreach (var spawnerFromFile in LoadSlavesFromFileByWorld(world))
            {
                spawnersFromFile.TryAdd(spawnerFromFile.Id, spawnerFromFile);
            }
            if (spawnersFromFile.ContainsKey(spawn.Id))
            {
                spawnersFromFile[spawn.Id] = spawn;
            }
            else
            {
                spawnersFromFile.Add(spawn.Id, spawn);
            }

            var jsonPathOut = Path.Combine(FileManager.AppPath, "Data", "Worlds", world.Name, "slave_spawns_new.json");
            var json = JsonConvert.SerializeObject(spawnersFromFile.Values.ToArray(), Formatting.Indented, new JsonModelsConverter());
            File.WriteAllText(jsonPathOut, json);
            SendMessage(character, "All slaves have been saved with added slave ObjId:{0}, TemplateId:{1}", slave.ObjId, slave.TemplateId);
        }

        private List<JsonSlaveSpawns> LoadSlavesFromFileByWorld(World world)
        {
            var jsonPathIn = Path.Combine(FileManager.AppPath, "Data", "Worlds", world.Name, "slave_spawns.json");
            if (!File.Exists(jsonPathIn))
            {
                throw new ApplicationException($"File {jsonPathIn} doesn't exists.");
            }

            var contents = FileManager.GetFileContents(jsonPathIn);
            _log.Info($"Loading spawns from file {jsonPathIn} ...");

            if (string.IsNullOrWhiteSpace(contents))
            {
                return new List<JsonSlaveSpawns>();
            }
            else
            {
                return JsonHelper.DeserializeObject<List<JsonSlaveSpawns>>(contents);
            }
        }
    }
}
