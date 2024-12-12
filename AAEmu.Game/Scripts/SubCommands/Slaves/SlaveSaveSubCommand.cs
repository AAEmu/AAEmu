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
using AAEmu.Game.Utils.Scripts.SubCommands;
using AAEmu.Commons.Exceptions;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Scripts.SubCommands.Slaves;

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

    public override void Execute(ICharacter character, string triggerArgument,
        IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
    {
        if (parameters.TryGetValue("ObjId", out var objId))
        {
            SaveById(character, objId, messageOutput);
        }
        else
        {
            SaveAll(character, messageOutput);
        }
    }

    private void SaveAll(ICharacter character, IMessageOutput messageOutput)
    {
        var currentWorld = WorldManager.Instance.GetWorld(((Character)character).Transform.WorldId);

        var allSlaves = WorldManager.Instance.GetAllSlaves();
        var slavesInWorld = WorldManager.Instance.GetAllSlavesFromWorld(currentWorld.Id);

        var slaveSpawnersFromFile = LoadSlavesFromFileByWorld(currentWorld);
        var slaveSpawnersToFile = slaveSpawnersFromFile.ToList();

        foreach (var npc in slavesInWorld.Where(n => n.Spawner is not null))
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

        var jsonPathOut =
            Path.Combine(FileManager.AppPath, "Data", "Worlds", currentWorld.Name, "slave_spawns_new.json");
        var json = JsonConvert.SerializeObject(slaveSpawnersToFile.ToArray(), Formatting.Indented,
            new JsonModelsConverter());
        File.WriteAllText(jsonPathOut, json);
        SendMessage(messageOutput, "All slaves have been saved!");
    }

    private void SaveById(ICharacter character, uint objId, IMessageOutput messageOutput)
    {
        var spawners = new List<JsonSlaveSpawns>();
        var slave = (Models.Game.Units.Slave)WorldManager.Instance.GetGameObject(objId);
        if (slave is null)
        {
            SendColorMessage(messageOutput, Color.Red, $"Slave with objId {objId} Does not exist");
            return;
        }

        var world = WorldManager.Instance.GetWorld(slave.Transform.WorldId);
        if (world is null)
        {
            SendColorMessage(messageOutput, Color.Red, $"Could not find the worldId {slave.Transform.WorldId}");
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

        Dictionary<uint, JsonSlaveSpawns> spawnersFromFile = [];
        foreach (var spawnerFromFile in LoadSlavesFromFileByWorld(world))
        {
            spawnersFromFile.TryAdd(spawnerFromFile.Id, spawnerFromFile);
        }

        if (!spawnersFromFile.TryAdd(spawn.Id, spawn))
        {
            spawnersFromFile[spawn.Id] = spawn;
        }

        var jsonPathOut = Path.Combine(FileManager.AppPath, "Data", "Worlds", world.Name, "slave_spawns_new.json");
        var json = JsonConvert.SerializeObject(spawnersFromFile.Values.ToArray(), Formatting.Indented,
            new JsonModelsConverter());
        File.WriteAllText(jsonPathOut, json);
        SendMessage(messageOutput,
            $"All slaves have been saved with added slave ObjId:{slave.ObjId}, TemplateId:{slave.TemplateId}");
    }

    private List<JsonSlaveSpawns> LoadSlavesFromFileByWorld(World world)
    {
        var jsonPathIn = Path.Combine(FileManager.AppPath, "Data", "Worlds", world.Name, "slave_spawns.json");
        if (!File.Exists(jsonPathIn))
        {
            throw new GameException($"File {jsonPathIn} doesn't exists.");
        }

        var contents = FileManager.GetFileContents(jsonPathIn);
        Logger.Info($"Loading spawns from file {jsonPathIn} ...");

        if (string.IsNullOrWhiteSpace(contents))
        {
            return [];
        }
        else
        {
            return JsonHelper.DeserializeObject<List<JsonSlaveSpawns>>(contents);
        }
    }
}
