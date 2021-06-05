using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Json;
using AAEmu.Game.Utils;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;

namespace AAEmu.Game.Scripts.Commands
{
    public class Nwrite : ICommand
    {
        protected static Logger _log = LogManager.GetCurrentClassLogger();
        public void OnLoad()
        {
            string[] name = { "nwrite", "nw"};
            CommandManager.Instance.Register(name, this);
        }

        public string GetCommandLineHelp()
        {
            return "(target)";
        }

        public string GetCommandHelpText()
        {
            return "write's npc's or doodad's current position / rotation to json";
        }

        public void Execute(Character character, string[] args)
        {
            var worlds = WorldManager.Instance.GetWorlds();
            var npcs = WorldManager.Instance.GetAllNpcs();

            if (args.Length > 0)
            {
                if (args[0].ToLower() == "doodad")
                {
                    try
                    {
                        if (uint.TryParse(args[1], out var id))
                        {
                            var doodad = WorldManager.Instance.GetDoodad(id);
                              if (doodad != null)
                            {
                                foreach (var world in worlds)
                                {
                                    if (doodad.Spawner.Position.WorldId == world.Id)
                                    {
                                        string path =
                                            ($"{FileManager.AppPath}Data/Worlds/{world.Name}/doodad_spawns.json");

                                        var contents =
                                            FileManager.GetFileContents(
                                                $"{FileManager.AppPath}Data/Worlds/{world.Name}/doodad_spawns.json");
                                        if (string.IsNullOrWhiteSpace(contents))
                                            _log.Warn(
                                                $"File {FileManager.AppPath}Data/Worlds/{world.Name}/doodad_spawns.json doesn't exists or is empty.");
                                        else
                                        {
                                            if (JsonHelper.TryDeserializeObject(contents,
                                                out List<JsonDoodadSpawns> spawners,
                                                out _))
                                            {
                                                if (doodad.Spawner.Id == 0) // spawned into the game manually
                                                {
                                                    var newId = (uint)((spawners[spawners.Count - 1].Id) + 1);
                                                    var pos = new Pos();
                                                    pos.X = doodad.Position.X;
                                                    pos.Y = doodad.Position.Y;
                                                    pos.Z = doodad.Position.Z;
                                                    pos.RotationX = doodad.Position.RotationX;
                                                    pos.RotationY = doodad.Position.RotationY;
                                                    pos.RotationZ = doodad.Position.RotationZ;

                                                    var newEntry = new JsonDoodadSpawns();
                                                    newEntry.Id = newId;
                                                    newEntry.UnitId = doodad.TemplateId;
                                                    newEntry.Position = pos;
                                                    newEntry.Scale = 1.0f;
                                                    spawners.Add(newEntry);

                                                    doodad.Spawner.Id = newId; //Set ID incase you edit it after adding!
                                                }
                                                else
                                                {
                                                    foreach (var spawner in spawners)
                                                    {
                                                        var pos = doodad.Position;

                                                        if (spawner.Id == doodad.Spawner.Id)
                                                        {
                                                            spawner.Position.X = pos.X;
                                                            spawner.Position.Y = pos.Y;
                                                            spawner.Position.Z = pos.Z;
                                                            spawner.Position.RotationX = pos.RotationX;
                                                            spawner.Position.RotationY = pos.RotationY;
                                                            spawner.Position.RotationZ = pos.RotationZ;
                                                            break;
                                                        }
                                                    }
                                                }

                                                string json = JsonConvert.SerializeObject(spawners.ToArray(),
                                                    Formatting.Indented);
                                                File.WriteAllText(path, json);
                                                character.SendMessage("[Nwrite] Doodad ObjId: {0} has been saved!",
                                                    doodad.ObjId);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                }
                else
                {
                    if (args[0].ToLower() == "all")
                    {
                        foreach (var world in worlds)
                        {
                            if (character.Position.WorldId == world.Id)
                            {
                                string path = ($"{FileManager.AppPath}Data/Worlds/{world.Name}/npc_spawns.json");

                                var contents =
                                    FileManager.GetFileContents(
                                        $"{FileManager.AppPath}Data/Worlds/{world.Name}/npc_spawns.json");
                                if (string.IsNullOrWhiteSpace(contents))
                                    _log.Warn(
                                        $"File {FileManager.AppPath}Data/Worlds/{world.Name}/npc_spawns.json doesn't exists or is empty.");
                                else
                                {
                                    if (JsonHelper.TryDeserializeObject(contents, out List<JsonNpcSpawns> spawners, out _))
                                    {
                                        for (var i = 0; i < npcs.Count; i++)
                                        {
                                            if (npcs[i].Spawner == null)
                                            {
                                                continue;
                                            }

                                            if (npcs[i].Spawner.Position.WorldId != character.Position.WorldId)
                                            {
                                                continue;
                                            }

                                            if (npcs[i].Spawner.Id == 0) // spawned into the game manually
                                            {
                                                var newId = (uint)((spawners[spawners.Count - 1].Id) + 1);
                                                var pos = new Pos();
                                                pos.X = npcs[i].Position.X;
                                                pos.Y = npcs[i].Position.Y;
                                                pos.Z = npcs[i].Position.Z;
                                                pos.RotationX = npcs[i].Position.RotationX;
                                                pos.RotationY = npcs[i].Position.RotationY;
                                                pos.RotationZ = npcs[i].Position.RotationZ;

                                                var newEntry = new JsonNpcSpawns();
                                                newEntry.Count = 1;
                                                newEntry.Id = newId;
                                                newEntry.UnitId = npcs[i].TemplateId;
                                                newEntry.Position = pos;
                                                newEntry.Scale = npcs[i].Scale; // 0.0 and 1.0 seem to do the same thing 
                                                spawners.Add(newEntry);

                                                npcs[i].Spawner.Id = newId; //Set ID incase you edit it after adding!
                                            }
                                            else
                                            {
                                                foreach (var spawner in spawners)
                                                {
                                                    var pos = npcs[i].Position;

                                                    if (spawner.Id == npcs[i].Spawner.Id)
                                                    {
                                                        spawner.Position.X = pos.X;
                                                        spawner.Position.Y = pos.Y;
                                                        spawner.Position.Z = pos.Z;
                                                        spawner.Position.RotationZ = pos.RotationZ;
                                                        break;
                                                    }
                                                }
                                            }
                                        }

                                        string json = JsonConvert.SerializeObject(spawners.ToArray(),
                                            Formatting.Indented);
                                        File.WriteAllText(path, json);
                                        character.SendMessage("[Nwrite] all npcs have been saved!");
                                    }
                                    else
                                        throw new Exception(
                                            $"SpawnManager: Parse {FileManager.AppPath}Data/Worlds/{world.Name}/npc_spawns.json file");
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                if (character.CurrentTarget != null && character.CurrentTarget != character)
                {
                    if (character.CurrentTarget is Npc npc)
                    {
                        foreach (var world in worlds)
                        {
                            if (npc.Spawner.Position.WorldId == world.Id)
                            {
                                string path = ($"{FileManager.AppPath}Data/Worlds/{world.Name}/npc_spawns.json");
                                
                                var contents =
                                    FileManager.GetFileContents(
                                        $"{FileManager.AppPath}Data/Worlds/{world.Name}/npc_spawns.json");
                                if (string.IsNullOrWhiteSpace(contents))
                                    _log.Warn(
                                        $"File {FileManager.AppPath}Data/Worlds/{world.Name}/npc_spawns.json doesn't exists or is empty.");
                                else
                                {
                                    if (JsonHelper.TryDeserializeObject(contents, out List<JsonNpcSpawns> spawners,
                                        out _))
                                    {
                                        if (npc.Spawner.Id == 0) // spawned into the game manually
                                        {
                                            var newId = (uint)((spawners[spawners.Count - 1].Id) + 1); 
                                            var pos = new Pos();
                                            pos.X = npc.Position.X;
                                            pos.Y = npc.Position.Y;
                                            pos.Z = npc.Position.Z;
                                            pos.RotationX = npc.Position.RotationX;
                                            pos.RotationY = npc.Position.RotationY;
                                            pos.RotationZ = npc.Position.RotationZ;

                                            var newEntry = new JsonNpcSpawns();
                                            newEntry.Count = 1;
                                            newEntry.Id = newId;
                                            newEntry.UnitId = npc.TemplateId;
                                            newEntry.Position = pos;
                                            newEntry.Scale = npc.Scale; // 0.0 and 1.0 seem to do the same thing 
                                            spawners.Add(newEntry);

                                            npc.Spawner.Id = newId; //Set ID incase you edit it after adding!
                                        }
                                        else
                                        {
                                            foreach (var spawner in spawners)
                                            {
                                                var pos = npc.Position;

                                                if (spawner.Id == npc.Spawner.Id)
                                                {
                                                    spawner.Position.X = pos.X;
                                                    spawner.Position.Y = pos.Y;
                                                    spawner.Position.Z = pos.Z;
                                                    spawner.Position.RotationZ = pos.RotationZ;
                                                    break;
                                                }
                                            }
                                        }

                                        string json = JsonConvert.SerializeObject(spawners.ToArray(),
                                            Formatting.Indented);
                                        File.WriteAllText(path, json);
                                        character.SendMessage("[Nwrite] Npc ObjId: {0} has been saved!",
                                            character.CurrentTarget.ObjId);
                                    }
                                    else
                                        throw new Exception(
                                            $"SpawnManager: Parse {FileManager.AppPath}Data/Worlds/{world.Name}/npc_spawns.json file");
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
