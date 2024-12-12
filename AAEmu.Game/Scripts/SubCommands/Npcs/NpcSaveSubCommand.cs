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

namespace AAEmu.Game.Scripts.SubCommands.Npcs;

public class NpcSaveSubCommand : SubCommandBase
{
    public NpcSaveSubCommand()
    {
        Title = "[Npc Save]";
        Description = "Save one or all npc positions in the current character world to the world npc spawns file";
        CallPrefix = $"{CommandManager.CommandPrefix}npc save";
        AddParameter(new StringSubCommandParameter("target", "target", true, "all", "id"));
        AddParameter(new NumericSubCommandParameter<uint>("ObjId", "object Id", false));
    }

    public override void Execute(ICharacter character, string triggerArgument,
        IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
    {
        if (parameters.TryGetValue("ObjId", out var npcObjId))
        {
            SaveById(character, npcObjId, messageOutput);
        }
        else
        {
            SaveAll(character, messageOutput);
        }
    }

    private void SaveAll(ICharacter character, IMessageOutput messageOutput)
    {
        var currentWorld = WorldManager.Instance.GetWorld(((Character)character).Transform.WorldId);
        var allNpcs = WorldManager.Instance.GetAllNpcs();
        var npcsInWorld = WorldManager.Instance.GetAllNpcsFromWorld(currentWorld.Id);
        var npcSpawnersFromFile = LoadNpcsFromFileByWorld(currentWorld);
        var npcSpawnersToFile = npcSpawnersFromFile.ToList();

        foreach (var npc in npcsInWorld.Where(n => n.Spawner is not null))
        {
            switch (npc.Spawner.Id)
            {
                // spawned into the game manually
                case 0:
                    {
                        var pos = npc.Transform.World;
                        var newNpcSpawn = new JsonNpcSpawns
                        {
                            Id = allNpcs.Last().ObjId++,
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

                        npcSpawnersToFile.Add(newNpcSpawn);
                        break;
                    }

                // removed from the game manually
                case 0xffffffff: //(uint)-1
                    {
                        // Do not add to the output of a manually remote Npc
                        var npcSpawnsToRemove = new List<JsonNpcSpawns>();

                        foreach (var npcSpawnerToFile in npcSpawnersToFile
                                     .Where(n => n.UnitId == npc.TemplateId))
                        {
                            // If the position changed don't mark to be removed
                            if (!npc.Transform.World.Position.Equals(npcSpawnerToFile.Position.AsVector3()))
                            {
                                continue;
                            }

                            npcSpawnsToRemove.Add(npcSpawnerToFile);
                            break;
                        }

                        foreach (var npcSpawn in npcSpawnsToRemove)
                        {
                            npcSpawnersToFile.Remove(npcSpawn);
                        }

                        break;
                    }
            }
        }

        var jsonPathOut = Path.Combine(FileManager.AppPath, "Data", "Worlds", currentWorld.Name, "npc_spawns_new.json");
        var json = JsonConvert.SerializeObject(npcSpawnersToFile.ToArray(), Formatting.Indented,
            new JsonModelsConverter());
        File.WriteAllText(jsonPathOut, json);
        SendMessage(messageOutput, "All npcs have been saved!");
    }

    private void SaveById(ICharacter character, uint npcObjId, IMessageOutput messageOutput)
    {
        var spawners = new List<JsonNpcSpawns>();
        var npc = WorldManager.Instance.GetNpc(npcObjId);
        if (npc is null)
        {
            SendColorMessage(messageOutput, Color.Red, $"Npc with objId {npcObjId} Does not exist");
            return;
        }

        var world = WorldManager.Instance.GetWorld(npc.Transform.WorldId);
        if (world is null)
        {
            SendColorMessage(messageOutput, Color.Red, $"Could not find the worldId {npc.Transform.WorldId}");
            return;
        }

        var spawn = new JsonNpcSpawns
        {
            Id = npc.ObjId,
            UnitId = npc.TemplateId,
            Position = new JsonPosition
            {
                X = npc.Transform.Local.Position.X,
                Y = npc.Transform.Local.Position.Y,
                Z = npc.Transform.Local.Position.Z,
                Roll = npc.Transform.Local.Rotation.X.RadToDeg(),
                Pitch = npc.Transform.Local.Rotation.Y.RadToDeg(),
                Yaw = npc.Transform.Local.Rotation.Z.RadToDeg()
            }
        };

        Dictionary<uint, JsonNpcSpawns> spawnersFromFile = [];
        foreach (var spawnerFromFile in LoadNpcsFromFileByWorld(world))
        {
            spawnersFromFile.TryAdd(spawnerFromFile.Id, spawnerFromFile);
        }

        if (!spawnersFromFile.TryAdd(spawn.Id, spawn))
        {
            spawnersFromFile[spawn.Id] = spawn;
        }

        var jsonPathOut = Path.Combine(FileManager.AppPath, "Data", "Worlds", world.Name, "npc_spawns_new.json");
        var json = JsonConvert.SerializeObject(spawnersFromFile.Values.ToArray(), Formatting.Indented,
            new JsonModelsConverter());
        File.WriteAllText(jsonPathOut, json);
        SendMessage(messageOutput,
            $"All npcs have been saved with added npc ObjId:{npc.ObjId}, TemplateId:{npc.TemplateId}");
    }

    private List<JsonNpcSpawns> LoadNpcsFromFileByWorld(World world)
    {
        var jsonPathIn = Path.Combine(FileManager.AppPath, "Data", "Worlds", world.Name, "npc_spawns.json");
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
            return JsonHelper.DeserializeObject<List<JsonNpcSpawns>>(contents);
        }
    }
}
