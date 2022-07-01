using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Json;
using Newtonsoft.Json;
using NLog;

namespace AAEmu.Game.Utils
{
    public class NpcCommandUtil
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Executes based in the main command and its remaining arguments
        /// </summary>
        /// <param name="character"></param>
        /// <param name="mainCommand">main command</param>
        /// <param name="args">main command arguments</param>
        public static void GetCommandChoice(Character character, string mainCommand, string[] args)
        {
            switch (mainCommand)
            {
                case "pos":
                    RePosition(character, args);
                    break;
                case "save":
                    Save(character, args);
                    break;
                case "remove":
                    Remove(character, args);
                    break;
                default:
                    character.SendMessage("[Npc] /npc save <ObjId> || /npc pos target x=<x> y=<y> z=<z> roll=<roll> pitch=<pitch> yaw=<yaw> - All positions are optional use all or only the ones you want to change");
                    break;
            }
        }

        private static void Remove(Character character, string[] args)
        {
            if (args.Length >= 1 && uint.TryParse(args.FirstOrDefault(), out var npcObjId))
            {
                var npc = WorldManager.Instance.GetNpc(npcObjId);
                if (npc != null)
                {
                    // Remove Npc
                    try
                    {
                        //npc.Spawner.Despawn(npc);
                        npc.Spawner.Id = 0xffffffff; // removed from the game manually (укажем, что не надо сохранять в файл npc_spawns_new.json командой /save all)
                        npc.Hide();
                    }
                    catch (Exception e)
                    {
                        character.SendMessage(e.Message);
                        _log.Warn(e);
                    }
                }
                else
                {
                    character.SendMessage("|cFFFF0000[Npc] Npc with objId {0} Does not exist |r", npcObjId);
                }
            }
            else
            {
                character.SendMessage("[Npc] /npc remove <ObjId>");
            }
        }

        private static void Save(Character character, string[] args)
        {
            var worlds = WorldManager.Instance.GetWorlds();
            var firstArgument = args.FirstOrDefault();

            
            if (firstArgument == "all") //save all
            {
                // Save all NPCs in the current world
                SaveAll(character, worlds);
            }
            else if (uint.TryParse(firstArgument, out var npcObjId)) //save <ObjId>
            {
                SaveById(character, worlds, npcObjId);
            }
            else
            {
                character.SendMessage("[Npc] /npc save <ObjId>");
            }
        }

        private static void SaveById(Character character, World[] worlds, uint npcObjId)
        {
            Npc npc;
            var spawners = new List<JsonNpcSpawns>();
            var npcSpawners = new Dictionary<uint, JsonNpcSpawns>();

            npc = WorldManager.Instance.GetNpc(npcObjId);
            if (npc is null)
            {
                character.SendMessage("|cFFFF0000[Npc] Npc with objId {0} Does not exist |r", npcObjId);
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

            var world = worlds.SingleOrDefault(w => w.Id == npc.Transform.WorldId);
            if (world is null)
            {
                character.SendMessage("|cFFFF0000[Npc] Could not find the worldId {0} |r", npc.Transform.WorldId);
                return;
            }

            try
            {
                var spawnersFromFile = LoadNpcsFromFileByWorld(world).ToDictionary(x => x.Id, x => x);

                if (spawnersFromFile.ContainsKey(spawn.Id))
                {
                    spawnersFromFile[spawn.Id] = spawn;
                }
                else
                {
                    spawnersFromFile.Add(spawn.Id, spawn);
                }

                var jsonPathOut = Path.Combine(FileManager.AppPath, "Data", "Worlds", world.Name, "npc_spawns_new.json");
                var json = JsonConvert.SerializeObject(npcSpawners.Values.ToArray(), Formatting.Indented);
                File.WriteAllText(jsonPathOut, json);
                character.SendMessage("[Npc] all npcs have been saved with added npc ObjId {0}, TemplateId {1}", npc.ObjId, npc.TemplateId);
                
            }
            catch (Exception e)
            {
                character.SendMessage(e.Message);
                _log.Warn(e);
            }
        }

        private static List<JsonNpcSpawns> LoadNpcsFromFileByWorld(World world)
        {
            List<JsonNpcSpawns> npcSpawnersFromFile;
            var jsonPathIn = Path.Combine(FileManager.AppPath, "Data", "Worlds", world.Name, "npc_spawns.json");
            if (!File.Exists(jsonPathIn))
            {
                throw new ApplicationException($"File {jsonPathIn} doesn't exists.");
            }

            var contents = FileManager.GetFileContents(jsonPathIn);
            _log.Info($"Loading spawns from file {jsonPathIn} ...");
            if (string.IsNullOrWhiteSpace(contents))
            {
                return new List<JsonNpcSpawns>();
            }
            else
            {
                return JsonHelper.DeserializeObject<List<JsonNpcSpawns>>(contents);
            }
        }

        private static void SaveAll(Character character, World[] worlds)
        {
            var npcSpawnersToFile = new List<JsonNpcSpawns>();
            var worldNpcSpawners = WorldManager.Instance.GetAllNpcs();
            foreach (var world in worlds)
            {
                try
                {
                    if (character.Transform.WorldId == world.Id)
                    {
                        var npcSpawnersFromFile = LoadNpcsFromFileByWorld(world);
                        {
                            foreach (var spawnerFromFile in npcSpawnersFromFile)
                            {
                                npcSpawnersToFile.Add(spawnerFromFile);
                            }

                            var npcsInCharactersWorld = worldNpcSpawners.Where(n => n.Spawner?.Position?.WorldId == character.Transform.WorldId);

                            foreach (var npcWorldSpawn in npcsInCharactersWorld)
                            {
                                switch (npcWorldSpawn.Spawner.Id)
                                {
                                    // spawned into the game manually
                                    case 0:
                                        {
                                            var pos = npcWorldSpawn.Transform.World;
                                            var newNpcSpawn = new JsonNpcSpawns
                                            {
                                                Id = worldNpcSpawners.Last().ObjId++,
                                                UnitId = npcWorldSpawn.TemplateId,
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
                                                .Where(n => n.UnitId == npcWorldSpawn.TemplateId))
                                            {
                                                if (!npcWorldSpawn.Transform.World.Position.Equals(npcSpawnerToFile.Position.AsVector3()))
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

                            var jsonPathOut = Path.Combine(FileManager.AppPath, "Data", "Worlds", world.Name, "npc_spawns_new.json");
                            var json = JsonConvert.SerializeObject(npcSpawnersToFile.ToArray(), Formatting.Indented);
                            File.WriteAllText(jsonPathOut, json);
                            character.SendMessage("[Npc] all npcs have been saved!");
                        }
                    }
                }
                catch (Exception e)
                {
                    _log.Error(e);
                    character.SendMessage(e.Message);
                }
            }
        }

        private static float GetPositionByArgument(Npc npc, string positionArgument, string[] args)
        {
            float newPosition;
            var argumentValue = args.Where(a => a.StartsWith(positionArgument + "=")).FirstOrDefault();
            if (argumentValue is null)
            {
                return GetCurrentNpcPositionByArgument(npc, positionArgument);
            }

            if (float.TryParse(argumentValue.Split('=')[1], out newPosition))
            {
                return newPosition;
            }
            else
            {
                throw new ArgumentException($"Invalid value for {argumentValue} position", nameof(positionArgument));
            }
        }

        private static float GetCurrentNpcPositionByArgument(Npc npc, string positionArgument)
        {
            switch (positionArgument)
            {
                case "x": return npc.Transform.Local.Position.X;
                case "y": return npc.Transform.Local.Position.Y;
                case "z": return npc.Transform.Local.Position.Z;
                case "yaw": 
                case "rz": return npc.Transform.Local.Rotation.Z;
                case "roll": 
                case "rx": return npc.Transform.Local.Rotation.X;
                case "pitch": 
                case "ry": return npc.Transform.Local.Rotation.Y;
                default: throw new ArgumentException("Invalid position",nameof(positionArgument));
            }
        }

        private static void RePosition(Character character, string[] args)
        {
            Npc npc;
            if (args.FirstOrDefault() == "target")
            {
                if (character.CurrentTarget is not null && character.CurrentTarget is Npc aNpc)
                {
                    npc = aNpc;
                }
                else 
                {
                    character.SendMessage("|cFFFF0000[Npc] Select a Npc target |r");
                    return;
                }
            }
            else if (uint.TryParse(args.FirstOrDefault(), out var npcObjId))
            {
                npc = WorldManager.Instance.GetNpc(npcObjId);
                if (npc == null)
                {
                    character.SendMessage("|cFFFF0000[Npc] Npc with objId {0} Does not exist |r", npcObjId);
                    return;
                }
            }
            else
            {
                character.SendMessage("|cFFFF0000[Npc] Invalid objId {0} should be a number |r", args.FirstOrDefault());
                return;
            }

            var x = GetPositionByArgument(npc, "x", args);
            var y = GetPositionByArgument(npc, "y", args);
            var z = GetPositionByArgument(npc, "z", args);
            var roll = GetPositionByArgument(npc, "roll", args);
            var pitch = GetPositionByArgument(npc, "pitch", args);
            var yaw = GetPositionByArgument(npc, "yaw", args);

            character.SendMessage("[Npc] Npc ObjId:{0} TemplateId:{1}, x:{2}, y:{3}, z:{4}, roll:{5}, pitch:{6}, yaw:{7}", npc.ObjId, npc.TemplateId, x, y, z, roll.RadToDeg(), pitch.RadToDeg(), yaw.RadToDeg());
            npc.Transform.Local.SetPosition(x, y, z, roll, pitch, yaw);
            var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);
            moveType.X = x;
            moveType.Y = y;
            moveType.Z = z;
            var characterRot = character.CurrentTarget.Transform.Local.ToRollPitchYawSBytes();
            moveType.RotationX = characterRot.Item1;
            moveType.RotationY = characterRot.Item2;
            moveType.RotationZ = characterRot.Item3;
            moveType.Flags = 5;
            moveType.DeltaMovement = new sbyte[3];
            moveType.DeltaMovement[0] = 0;
            moveType.DeltaMovement[1] = 0;
            moveType.DeltaMovement[2] = 0;
            moveType.Stance = 1;    // combat=0, idle=1
            moveType.Alertness = 0; // idle=0, combat=2
            moveType.Time += 50;    // has to change all the time for normal motion.
            character.BroadcastPacket(new SCOneUnitMovementPacket(character.CurrentTarget.ObjId, moveType), true);
        }
    }
}
