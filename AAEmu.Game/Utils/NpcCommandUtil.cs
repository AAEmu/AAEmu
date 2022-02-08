using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
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
        public static void GetCommandChoice(Character character, string choice, string[] args)
        {
            uint templateId;
            uint npcObjId;
            uint skillId = 0;
            uint phase = 0;
            var worlds = WorldManager.Instance.GetWorlds();
            Npc npc = null;

            switch (choice)
            {
                case "pos":
                    // NPC by targetting
                    if ((args.Length == 7) && (character.CurrentTarget is Npc aNpc))
                    {
                        npc = aNpc;

                        // 0   1 2 3 4  5  6
                        // pos x y z rx ry rz
                        float value = 0;
                        float x = npc.Transform.Local.Position.X;
                        float y = npc.Transform.Local.Position.Y;
                        float z = npc.Transform.Local.Position.Z;
                        var roll = npc.Transform.Local.Rotation.X;
                        var pitch = npc.Transform.Local.Rotation.Y;
                        var yaw = npc.Transform.Local.Rotation.Z;

                        if (args[1] != "x" && float.TryParse(args[1], out value))
                        {
                            x = value;
                        }
                        if (args[2] != "y" && float.TryParse(args[2], out value))
                        {
                            y = value;
                        }
                        if (args[3] != "z" && float.TryParse(args[3], out value))
                        {
                            z = value;
                        }
                        if (args[4] != "rx" && float.TryParse(args[4], out value))
                        {
                            roll = value.DegToRad();
                        }
                        if (args[5] != "ry" && float.TryParse(args[5], out value))
                        {
                            pitch = value.DegToRad();
                        }
                        if (args[6] != "rz" && float.TryParse(args[6], out value))
                        {
                            yaw = value.DegToRad();
                        }

                        character.SendMessage("[Npc] Npc ObjId: {0} TemplateId:{1}, x:{2}, y:{3}, z:{4}, rx:{5}, ry:{6}, rz:{7}",
                            npc.ObjId, npc.TemplateId, x, y, z, roll.RadToDeg(), pitch.RadToDeg(), yaw.RadToDeg());

                        npc.Transform.Local.SetPosition(x, y, z, roll, pitch, yaw);

                        var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);

                        moveType.X = x;
                        moveType.Y = y;
                        moveType.Z = z;

                        //character.CurrentTarget.Transform.Local.SetRotationDegree(roll, pitch, yaw);

                        var characterRot = character.CurrentTarget.Transform.Local.ToRollPitchYawSBytes();
                        moveType.RotationX = characterRot.Item1;
                        moveType.RotationY = characterRot.Item2;
                        moveType.RotationZ = characterRot.Item3;

                        moveType.Flags = 5;
                        moveType.DeltaMovement = new sbyte[3];
                        moveType.DeltaMovement[0] = 0;
                        moveType.DeltaMovement[1] = 0;
                        moveType.DeltaMovement[2] = 0;
                        moveType.Stance = 1;    //combat=0, idle=1
                        moveType.Alertness = 0; //idle=0, combat=2
                        moveType.Time += 50;    // has to change all the time for normal motion.

                        character.BroadcastPacket(new SCOneUnitMovementPacket(character.CurrentTarget.ObjId, moveType), true);
                    }

                    // NPC by objId
                    else if (args.Length == 5)
                    {
                        if (uint.TryParse(args[1], out npcObjId))
                        {
                            npc = WorldManager.Instance.GetNpc(npcObjId);
                            if ((npc != null) && (npc is Npc))
                            {
                                // берем в таргет Npc
                                character.BroadcastPacket(
                                    new SCTargetChangedPacket(
                                        character.ObjId,
                                        npcObjId),
                                    true);

                                character.CurrentTarget = npc;

                                // 0   1     2 3 4 5  6  7
                                // pos objId x y z rx ry rz
                                float value = 0;
                                float x = npc.Transform.Local.Position.X;
                                float y = npc.Transform.Local.Position.Y;
                                float z = npc.Transform.Local.Position.Z;
                                var roll = npc.Transform.Local.Rotation.X;
                                var pitch = npc.Transform.Local.Rotation.Y;
                                var yaw = npc.Transform.Local.Rotation.Z;

                                if (args[2] != "x" && float.TryParse(args[2], out value))
                                {
                                    x = value;
                                }
                                if (args[3] != "y" && float.TryParse(args[3], out value))
                                {
                                    y = value;
                                }
                                if (args[4] != "z" && float.TryParse(args[4], out value))
                                {
                                    z = value;
                                }

                                character.SendMessage("[Npc] Npc ObjId: {0} TemplateId:{1}, x:{2}, y:{3}, z:{4}, rx:{5}, ry:{6}, rz:{7}",
                                    npc.ObjId, npc.TemplateId, x, y, z, roll.RadToDeg(), pitch.RadToDeg(), yaw.RadToDeg());

                                npc.Transform.Local.SetPosition(x, y, z, roll, pitch, yaw);

                                var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);

                                moveType.X = x;
                                moveType.Y = y;
                                moveType.Z = z;

                                //character.CurrentTarget.Transform.Local.SetRotationDegree(x, y, z);

                                var characterRot = character.CurrentTarget.Transform.Local.ToRollPitchYawSBytes();
                                moveType.RotationX = characterRot.Item1;
                                moveType.RotationY = characterRot.Item2;
                                moveType.RotationZ = characterRot.Item3;

                                moveType.Flags = 5;
                                moveType.DeltaMovement = new sbyte[3];
                                moveType.DeltaMovement[0] = 0;
                                moveType.DeltaMovement[1] = 0;
                                moveType.DeltaMovement[2] = 0;
                                moveType.Stance = 1;    //combat=0, idle=1
                                moveType.Alertness = 0; //idle=0, combat=2
                                moveType.Time += 50;    // has to change all the time for normal motion.

                                character.BroadcastPacket(new SCOneUnitMovementPacket(character.CurrentTarget.ObjId, moveType), true);
                            }
                            else
                            {
                                character.SendMessage("|cFFFF0000[Npc] Npc with objId {0} Does not exist |r", npcObjId);
                            }
                        }
                        else
                        {
                            character.SendMessage("[Npc] /npc pos <ObjId> <x> <y> <z> - Use x y z instead of a value to keep current position");
                        }
                    }
                    else if (args.Length >= 8)
                    {
                        if (uint.TryParse(args[1], out npcObjId))
                        {
                            npc = WorldManager.Instance.GetNpc(npcObjId);

                            if ((npc != null) && (npc is Npc))
                            {
                                // берем в таргет Npc
                                character.BroadcastPacket(
                                    new SCTargetChangedPacket(
                                        character.ObjId,
                                        npcObjId),
                                    true);

                                character.CurrentTarget = npc;
                                
                                // 0   1     2 3 4 5  6  7
                                // pos objId x y z rx ry rz
                                float value = 0;
                                float x = npc.Transform.Local.Position.X;
                                float y = npc.Transform.Local.Position.Y;
                                float z = npc.Transform.Local.Position.Z;
                                var roll = npc.Transform.Local.Rotation.X;
                                var pitch = npc.Transform.Local.Rotation.Y;
                                var yaw = npc.Transform.Local.Rotation.Z;

                                if (args[2] != "x" && float.TryParse(args[2], out value))
                                {
                                    x = value;
                                }
                                if (args[3] != "y" && float.TryParse(args[3], out value))
                                {
                                    y = value;
                                }
                                if (args[4] != "z" && float.TryParse(args[4], out value))
                                {
                                    z = value;
                                }
                                if (args[5] != "rx" && float.TryParse(args[5], out value))
                                {
                                    roll = value.DegToRad();
                                }
                                if (args[6] != "ry" && float.TryParse(args[6], out value))
                                {
                                    pitch = value.DegToRad();
                                }
                                if (args[7] != "rz" && float.TryParse(args[7], out value))
                                {
                                    yaw = value.DegToRad();
                                }

                                character.SendMessage("[Npc] Npc ObjId: {0} TemplateId:{1}, x:{2}, y:{3}, z:{4}, rx:{5}, ry:{6}, rz:{7}",
                                    npc.ObjId, npc.TemplateId, x, y, z, roll.RadToDeg(), pitch.RadToDeg(), yaw.RadToDeg());

                                npc.Transform.Local.SetPosition(x, y, z, roll, pitch, yaw);

                                var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);

                                moveType.X = x;
                                moveType.Y = y;
                                moveType.Z = z;

                                //character.CurrentTarget.Transform.Local.SetRotationDegree(roll, pitch, yaw);

                                var characterRot = character.CurrentTarget.Transform.Local.ToRollPitchYawSBytes();
                                moveType.RotationX = characterRot.Item1;
                                moveType.RotationY = characterRot.Item2;
                                moveType.RotationZ = characterRot.Item3;

                                moveType.Flags = 5;
                                moveType.DeltaMovement = new sbyte[3];
                                moveType.DeltaMovement[0] = 0;
                                moveType.DeltaMovement[1] = 0;
                                moveType.DeltaMovement[2] = 0;
                                moveType.Stance = 1;    //combat=0, idle=1
                                moveType.Alertness = 0; //idle=0, combat=2
                                moveType.Time += 50;    // has to change all the time for normal motion.

                                character.BroadcastPacket(new SCOneUnitMovementPacket(character.CurrentTarget.ObjId, moveType), true);
                            }
                            else
                            {
                                character.SendMessage("|cFFFF0000[Npc] Npc with objId {0} Does not exist |r", npcObjId);
                            }
                        }
                        else
                        {
                            character.SendMessage("[Npc] /npc pos <ObjId> <x> <y> <z> <rx> <ry> <rz> - Use x y z roll pitch yaw instead of a value to keep current position");
                        }
                    }
                    else
                    {
                        character.SendMessage("[Npc] /npc pos <ObjId> <x> <y> <z> <rx> <ry> <rz> - Use x y z roll pitch yaw instead of a value to keep current position");
                    }
                    break;
                case "save":
                    if (args.Length >= 2)
                    {
                        var spawners = new List<JsonNpcSpawns>();
                        var npcSpawners = new Dictionary<uint, JsonNpcSpawns>();
                        // 0    1
                        // save objId
                        if (uint.TryParse(args[1], out npcObjId))
                        {
                            npc = WorldManager.Instance.GetNpc(npcObjId);
                            if ((npc != null) && (npc is Npc))
                            {
                                // Save Npc
                                try
                                {
                                    foreach (var world in worlds)
                                    {
                                        // Load Npc spawns
                                        _log.Info("Loading spawns...");
                                        var contents = string.Empty;
                                        var worldPath = Path.Combine(FileManager.AppPath, "Data", "Worlds", world.Name);
                                        var jsonFileName = string.Empty;
                                        jsonFileName = Path.Combine(worldPath, "npc_spawns_new.json");

                                        if (!File.Exists(jsonFileName))
                                        {
                                            _log.Info("World  {0}  is missing  {1}", world.Name, Path.GetFileName(jsonFileName));
                                        }
                                        else
                                        {
                                            contents = FileManager.GetFileContents(jsonFileName);
                                            if (string.IsNullOrWhiteSpace(contents))
                                                _log.Warn("File {0} is empty.", jsonFileName);
                                            else
                                            {
                                                if (JsonHelper.TryDeserializeObject(contents, out spawners, out _))
                                                {
                                                    foreach (var spawner in spawners)
                                                    {
                                                        npcSpawners.Add(spawner.Id, spawner);
                                                    }
                                                }
                                                else
                                                    throw new Exception(string.Format("SpawnManager: Parse {0} file", jsonFileName));

                                                // добавим измененный Npc
                                                // NPC by targetting
                                                if ((args.Length <= 0) && (character.CurrentTarget is Npc bNpc))
                                                    npc = bNpc;

                                                // NPC by objId
                                                if ((args.Length >= 2) && (args[0].ToLower() == "npc") && (uint.TryParse(args[1], out npcObjId)))
                                                    npc = WorldManager.Instance.GetNpc(npcObjId);

                                                if ((npc != null) && (npc is Npc))
                                                {
                                                    var spawn = new JsonNpcSpawns();

                                                    spawn.Position = new Pos();

                                                    spawn.Id = npc.ObjId;
                                                    spawn.UnitId = npc.TemplateId;

                                                    spawn.Position.X = npc.Transform.Local.Position.X;
                                                    spawn.Position.Y = npc.Transform.Local.Position.Y;
                                                    spawn.Position.Z = npc.Transform.Local.Position.Z;

                                                    spawn.Position.Roll = npc.Transform.Local.Rotation.X.RadToDeg();
                                                    spawn.Position.Pitch = npc.Transform.Local.Rotation.Y.RadToDeg();
                                                    spawn.Position.Yaw = npc.Transform.Local.Rotation.Z.RadToDeg();

                                                    if (npcSpawners.ContainsKey(spawn.Id))
                                                    {
                                                        npcSpawners[spawn.Id] = spawn;
                                                    }
                                                    else
                                                    {
                                                        npcSpawners.Add(spawn.Id, spawn);
                                                    }
                                                }

                                                var serialized = JsonConvert.SerializeObject(npcSpawners.Values.ToArray(), Formatting.Indented);
                                                FileManager.SaveFile(serialized, string.Format(jsonFileName, FileManager.AppPath));
                                                character.SendMessage("[Npc] Npc ObjId: {0} has been saved!", npc.ObjId);
                                            }
                                        }
                                    }
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
                            character.SendMessage("[Npc] /npc save <ObjId>");
                        }
                    }
                    else
                    {
                        character.SendMessage("[Npc] /npc save <ObjId>");
                    }
                    break;
                default:
                    character.SendMessage("[Npc] /npc save <ObjId> || pos <ObjId> <x> <y> <z> <rx> <ry> <rz> - Use x y z roll pitch yaw instead of a value to keep current position");
                    break;
            }
        }
    }
}
