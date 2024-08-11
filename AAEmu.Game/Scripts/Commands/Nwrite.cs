using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Json;
using NLog;
using System;
using System.Collections.Generic;
using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using Newtonsoft.Json;
using System.IO;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Scripts.Commands;

public class Nwrite : ICommand
{
    public string[] CommandNames { get; set; } = new string[] { "nwrite", "nw" };

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "(target) [all||[<doodad||npc> <objectId>]] ";
    }

    public string GetCommandHelpText()
    {
        return "Writes All or given Npc or given Doodad's current position and rotation to json";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var worlds = WorldManager.Instance.GetWorlds();
        var npcs = WorldManager.Instance.GetAllNpcs();

        Doodad doodad = null;
        Npc npc = null;

        // Select what we're editing
        // Doodad by Id ?
        if (args.Length >= 2 && args[0].ToLower() == "doodad" &&
            uint.TryParse(args[1], out var targetDoodadId))
        {
            doodad = WorldManager.Instance.GetDoodad(targetDoodadId);
        }

        // NPC by targeting
        if (args.Length <= 0 && character.CurrentTarget is Npc aNpc)
        {
            npc = aNpc;
        }

        // NPC by Id ?
        if (args.Length >= 2 && args[0].ToLower() == "npc" && uint.TryParse(args[1], out var targetNpcId))
        {
            npc = WorldManager.Instance.GetNpc(targetNpcId);
        }

        // All Npcs ?
        var saveAll = args.Length >= 1 && args[0].ToLower() == "all";

        if (doodad == null && npc == null && saveAll == false)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        if (doodad != null)
        {
            // Save target Doodad
            try
            {
                foreach (var world in worlds)
                {
                    var jsonPath = Path.Combine(FileManager.AppPath, "Data", "Worlds", world.Name,
                        "doodad_spawns.json");
                    if (doodad.Spawner.Position.WorldId == world.Id)
                    {
                        var contents = FileManager.GetFileContents(jsonPath);
                        if (string.IsNullOrWhiteSpace(contents))
                        {
                            CommandManager.SendErrorText(this, messageOutput,
                                $"File {jsonPath} doesn't exists or is empty.");
                        }
                        else
                        {
                            if (!JsonHelper.TryDeserializeObject(contents, out List<JsonDoodadSpawns> spawners, out _))
                            {
                                continue;
                            }

                            if (doodad.Spawner.Id == 0) // spawned into the game manually
                            {
                                var newId = spawners[^1].Id + 1;
                                var pos = new JsonPosition
                                {
                                    X = doodad.Transform.World.Position.X,
                                    Y = doodad.Transform.World.Position.Y,
                                    Z = doodad.Transform.World.Position.Z,
                                    Roll =
                                        (float)MathUtil.ClampDegAngle(doodad.Transform.Local.Rotation.X.RadToDeg()),
                                    Pitch =
                                        (float)MathUtil.ClampDegAngle(doodad.Transform.Local.Rotation.Y.RadToDeg()),
                                    Yaw = (float)MathUtil.ClampDegAngle(
                                        doodad.Transform.Local.Rotation.Z.RadToDeg())
                                };

                                var newEntry = new JsonDoodadSpawns
                                {
                                    Id = newId, UnitId = doodad.TemplateId, Position = pos
                                };
                                spawners.Add(newEntry);

                                doodad.Spawner.Id = newId; //Set ID in case you edit it after adding!
                            }
                            else
                            {
                                foreach (var spawner in spawners)
                                {
                                    if (spawner.Id == doodad.Spawner.Id)
                                    {
                                        spawner.Position.X = doodad.Transform.World.Position.X;
                                        spawner.Position.Y = doodad.Transform.World.Position.Y;
                                        spawner.Position.Z = doodad.Transform.World.Position.Z;
                                        spawner.Position.Roll =
                                            (float)MathUtil.ClampDegAngle(doodad.Transform.Local.Rotation.X.RadToDeg());
                                        spawner.Position.Pitch =
                                            (float)MathUtil.ClampDegAngle(doodad.Transform.Local.Rotation.Y.RadToDeg());
                                        spawner.Position.Yaw =
                                            (float)MathUtil.ClampDegAngle(doodad.Transform.Local.Rotation.Z.RadToDeg());
                                        break;
                                    }
                                }
                            }

                            var json = JsonConvert.SerializeObject(spawners.ToArray(), Formatting.Indented);
                            File.WriteAllText(jsonPath, json);
                            CommandManager.SendNormalText(this, messageOutput,
                                $"Doodad ObjId: {doodad.ObjId} has been saved!");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                CommandManager.SendErrorText(this, messageOutput, $"Exception: {e.Message}");
            }
        }
        else if (saveAll)
        {
            // Save all NPCs in the current world
            foreach (var world in worlds)
            {
                try
                {
                    if (character.Transform.WorldId == world.Id)
                    {
                        var jsonPath = Path.Combine(FileManager.AppPath, "Data", "Worlds", world.Name,
                            "npc_spawns.json");

                        var contents = FileManager.GetFileContents(jsonPath);
                        if (string.IsNullOrWhiteSpace(contents))
                        {
                            CommandManager.SendErrorText(this, messageOutput,
                                $"File {jsonPath} doesn't exists or is empty.");
                        }
                        else
                        {
                            if (JsonHelper.TryDeserializeObject(contents, out List<JsonNpcSpawns> spawners, out _))
                            {
                                for (var i = 0; i < npcs.Count; i++)
                                {
                                    if (npcs[i].Spawner == null)
                                    {
                                        continue; // No spawner attached
                                    }

                                    if (npcs[i].Spawner.Position.WorldId != character.Transform.WorldId)
                                    {
                                        continue; // wrong world
                                    }

                                    if (npcs[i].Spawner.Id == 0) // spawned into the game manually
                                    {
                                        var newId = spawners[^1].Id + 1;

                                        var pos = npcs[i].Transform.World;

                                        var newEntry = new JsonNpcSpawns();
                                        newEntry.Position = new JsonPosition();
                                        //newEntry.Count = 1;
                                        newEntry.Id = newId;
                                        newEntry.UnitId = npcs[i].TemplateId;
                                        newEntry.Position.X = pos.Position.X;
                                        newEntry.Position.Y = pos.Position.Y;
                                        newEntry.Position.Z = pos.Position.Z;
                                        newEntry.Position.Yaw =
                                            (float)MathUtil.ClampDegAngle(pos.Rotation.Z.RadToDeg());
                                        //var (rx, ry, rz) = pos.ToRollPitchYawSBytesMovement();
                                        //newEntry.Position.RotationX = rx;
                                        //newEntry.Position.RotationY = ry;
                                        //newEntry.Position.RotationZ = rz;
                                        //newEntry.Scale = npcs[i].Scale; // 0.0 and 1.0 seem to do the same thing 
                                        spawners.Add(newEntry);

                                        npcs[i].Spawner.Id = newId; //Set ID in-case you edit it after adding!
                                    }
                                    else
                                    {
                                        foreach (var spawner in spawners)
                                        {
                                            if (spawner.Id == npc.Spawner.Id)
                                            {
                                                var pos = npc.Transform.World;

                                                spawner.Position.X = pos.Position.X;
                                                spawner.Position.Y = pos.Position.Y;
                                                spawner.Position.Z = pos.Position.Z;
                                                spawner.Position.Yaw =
                                                    (float)MathUtil.ClampDegAngle(pos.Rotation.Z.RadToDeg());
                                                //var (rx, ry, rz) = pos.ToRollPitchYawSBytesMovement();
                                                //spawner.Position.RotationX = rx;
                                                //spawner.Position.RotationY = ry;
                                                //spawner.Position.RotationZ = rz;
                                                break;
                                            }
                                        }
                                    }
                                }

                                var json = JsonConvert.SerializeObject(spawners.ToArray(), Formatting.Indented);
                                File.WriteAllText(jsonPath, json);
                                CommandManager.SendNormalText(this, messageOutput, "All npcs have been saved!");
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    CommandManager.SendErrorText(this, messageOutput, $"Exception: {e.Message}");
                }
            }
        }
        else if (npc != null)
        {
            foreach (var world in worlds)
            {
                try
                {
                    if (npc.Spawner.Position.WorldId == world.Id)
                    {
                        var jsonPath = $"{FileManager.AppPath}Data/Worlds/{world.Name}/npc_spawns_new.json";

                        var contents = FileManager.GetFileContents(jsonPath);
                        if (string.IsNullOrWhiteSpace(contents))
                        {
                            CommandManager.SendErrorText(this, messageOutput,
                                $"File {jsonPath} doesn't exists or is empty.");
                        }
                        else
                        {
                            if (JsonHelper.TryDeserializeObject(contents, out List<JsonNpcSpawns> spawners, out _))
                            {
                                // Is it a new spawner ?
                                if (npc.Spawner.Id == 0) // spawned into the game manually
                                {
                                    var newId = spawners[^1].Id + 1;
                                    var pos = npc.Transform.World;

                                    var newEntry = new JsonNpcSpawns();
                                    //newEntry.Count = 1;
                                    newEntry.Id = newId;
                                    newEntry.UnitId = npc.TemplateId;
                                    newEntry.Position.X = pos.Position.X;
                                    newEntry.Position.Y = pos.Position.Y;
                                    newEntry.Position.Z = pos.Position.Z;
                                    //var (rx, ry, rz) = pos.ToRollPitchYawSBytesMovement();
                                    //newEntry.Position.RotationX = rx;
                                    //newEntry.Position.RotationY = ry;
                                    //newEntry.Position.RotationZ = rz;
                                    //newEntry.Scale = npc.Scale; // 0.0 and 1.0 seem to do the same thing 
                                    spawners.Add(newEntry);

                                    npc.Spawner.Id = newId; // Set ID in case you edit it after adding!
                                }
                                else
                                {
                                    // Find the spawner in the list
                                    foreach (var spawner in spawners)
                                    {
                                        var pos = npc.Transform.World;

                                        if (spawner.Id == npc.Spawner.Id)
                                        {
                                            spawner.Position.X = pos.Position.X;
                                            spawner.Position.Y = pos.Position.Y;
                                            spawner.Position.Z = pos.Position.Z;
                                            //var (rx, ry, rz) = pos.ToRollPitchYawSBytesMovement();
                                            //spawner.Position.RotationX = rx;
                                            //spawner.Position.RotationY = ry;
                                            //spawner.Position.RotationZ = rz;
                                            break;
                                        }
                                    }
                                }
                            }

                            var json = JsonConvert.SerializeObject(spawners.ToArray(), Formatting.Indented);
                            File.WriteAllText(jsonPath, json);
                            CommandManager.SendNormalText(this, messageOutput, "Npc data has been saved!");
                        }
                    }
                }
                catch (Exception e)
                {
                    CommandManager.SendErrorText(this, messageOutput, $"Exception: {e.Message}");
                }
            }
        }
        else
        {
            character.SendMessage("[Nwrite] I don't know what to do here");
        }
    }
}
