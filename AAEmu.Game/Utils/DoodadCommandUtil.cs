using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Json;

using Newtonsoft.Json;

using NLog;

namespace AAEmu.Game.Utils
{
    public class DoodadCommandUtil
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        public static void GetCommandChoice(Character character, string choice, string[] args)
        {
            uint templateId;
            uint doodadObjId;
            uint skillId = 0;
            int phase = 0;
            var worlds = WorldManager.Instance.GetWorlds();
            Doodad doodad = null;

            switch (choice)
            {
                case "chain":
                    if (args.Length >= 2)
                    {
                        if (uint.TryParse(args[1], out templateId))
                        {
                            doodad = DoodadManager.Instance.Create(0, templateId);
                            if (doodad == null)
                            {
                                character.SendMessage("|cFFFF0000[Doodad] Doodad with templateId {0} Does not found |r", templateId);
                            }
                            character.SendMessage("[Doodad] Phase chain, see to log");
                            _log.Warn("[Doodad] Chain: TemplateId {0}", templateId);
                            try
                            {
                                var doodadFuncGroups = DoodadManager.Instance.GetDoodadFuncGroups(templateId);
                                foreach (var doodadFuncGroup in doodadFuncGroups)
                                {
                                    // Display all functions that are available
                                    doodad.FuncGroupId = doodadFuncGroup.Id;
                                    _log.Info("[Doodad] FuncGroupId : {0}", doodad.FuncGroupId);
                                    // Get all doodad_phase_funcs
                                    var phaseFuncs = DoodadManager.Instance.GetPhaseFunc(doodad.FuncGroupId);
                                    if (phaseFuncs.Length == 0)
                                    {
                                        _log.Info("[Doodad] PhaseFunc: GroupId {0}, FuncId 0", doodad.FuncGroupId);
                                    }
                                    else
                                    {
                                        foreach (var phaseFunc in phaseFuncs)
                                        {
                                            // phaseFunc.Use
                                            _log.Info("[Doodad] PhaseFunc: GroupId {0}, FuncId {1}, FuncType {2}", phaseFunc.GroupId, phaseFunc.FuncId, phaseFunc.FuncType);
                                        }
                                    }
                                    // Get all doodad_funcs
                                    var doodadFuncs = DoodadManager.Instance.GetDoodadFuncs(doodad.FuncGroupId);
                                    if (doodadFuncs.Count == 0)
                                    {
                                        _log.Info("[Doodad] Func: GroupId {0}, FuncId 0", doodad.FuncGroupId);
                                    }
                                    else
                                    {
                                        foreach (var func in doodadFuncs)
                                        {
                                            // func.Use
                                            _log.Info("[Doodad] Func: GroupId {0}, FuncId {1}, FuncType {2}, NextPhase {3}, Skill {4}", func.GroupId, func.FuncId, func.FuncType, func.NextPhase, func.SkillId);
                                        }
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                _log.Fatal(e, "[Doodad] Doodad func crashed !");
                            }
                        }
                    }
                    else
                    {
                        character.SendMessage("[Doodad] /doodad chain <templateId>");
                    }
                    break;
                case "phase":
                case "setphase":
                    if (args.Length >= 3)
                    {
                        if (uint.TryParse(args[1], out doodadObjId))
                        {
                            if (int.TryParse(args[2], out phase))
                            {
                                doodad = WorldManager.Instance.GetDoodad(doodadObjId);
                                if ((doodad != null) && (doodad is Doodad))
                                {
                                    var listIds = DoodadManager.Instance.GetDoodadFuncGroupsId(doodad.TemplateId);
                                    character.SendMessage("[Doodad] SetPhase {0}", phase);
                                    character.SendMessage("[Doodad] TemplateId {0}: ObjId{1}, SetPhase {2}, Phases({3})", doodad.TemplateId, doodad.ObjId, phase, string.Join(", ", listIds));
                                    _log.Warn("[Doodad] Chain: TemplateId {0}, doodadObjId {1}, SetPhase {2}, Phases({3}", doodad.TemplateId, doodad.ObjId, phase, string.Join(", ", listIds));
                                    doodad.DoPhaseFuncs(character, phase);
                                }
                                else
                                {
                                    character.SendMessage("|cFFFF0000[Doodad] Doodad with objId {0} Does not exist |r", doodadObjId);
                                }
                            }
                        }
                        else
                        {
                            character.SendMessage("[Doodad] /doodad setphase <ObjId> <Phase>");
                        }
                    }
                    else
                    {
                        character.SendMessage("[Doodad] /doodad setphase <ObjId> <Phase>");
                    }
                    break;
                case "pos":
                    if (args.Length == 5)
                    {
                        if (uint.TryParse(args[1], out doodadObjId))
                        {
                            doodad = WorldManager.Instance.GetDoodad(doodadObjId);
                            if ((doodad != null) && (doodad is Doodad))
                            {
                                // 0   1     2 3 4 5  6  7
                                // pos objId x y z rx ry rz
                                float value = 0;
                                float x = doodad.Transform.Local.Position.X;
                                float y = doodad.Transform.Local.Position.Y;
                                float z = doodad.Transform.Local.Position.Z;
                                var roll = doodad.Transform.Local.Rotation.X;
                                var pitch = doodad.Transform.Local.Rotation.Y;
                                var yaw = doodad.Transform.Local.Rotation.Z;

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

                                character.SendMessage("[Doodad] Doodad ObjId: {0} TemplateId:{1}, x:{2}, y:{3}, z:{4}, rx:{5}, ry:{6}, rz:{7}",
                                    doodad.ObjId, doodad.TemplateId, x, y, z, roll.RadToDeg(), pitch.RadToDeg(), yaw.RadToDeg());

                                doodad.Transform.Local.SetPosition(x, y, z, roll, pitch, yaw);

                                doodad.Hide();
                                doodad.Show();
                            }
                            else
                            {
                                character.SendMessage("|cFFFF0000[Doodad] Doodad with objId {0} Does not exist |r", doodadObjId);
                            }
                        }
                        else
                        {
                            character.SendMessage("[Doodad] /doodad pos <ObjId> <x> <y> <z> - Use x y z instead of a value to keep current position");
                        }
                    }
                    else if (args.Length >= 8)
                    {
                        if (uint.TryParse(args[1], out doodadObjId))
                        {
                            doodad = WorldManager.Instance.GetDoodad(doodadObjId);
                            if ((doodad != null) && (doodad is Doodad))
                            {
                                // 0   1     2 3 4 5  6  7
                                // pos objId x y z rx ry rz
                                float value = 0;
                                float x = doodad.Transform.Local.Position.X;
                                float y = doodad.Transform.Local.Position.Y;
                                float z = doodad.Transform.Local.Position.Z;
                                var roll = doodad.Transform.Local.Rotation.X;
                                var pitch = doodad.Transform.Local.Rotation.Y;
                                var yaw = doodad.Transform.Local.Rotation.Z;

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

                                character.SendMessage("[Doodad] Doodad ObjId: {0} TemplateId:{1}, x:{2}, y:{3}, z:{4}, rx:{5}, ry:{6}, rz:{7}",
                                    doodad.ObjId, doodad.TemplateId, x, y, z, roll.RadToDeg(), pitch.RadToDeg(), yaw.RadToDeg());

                                doodad.Transform.Local.SetPosition(x, y, z, roll, pitch, yaw);

                                doodad.Hide();
                                doodad.Show();
                            }
                            else
                            {
                                character.SendMessage("|cFFFF0000[Doodad] Doodad with objId {0} Does not exist |r", doodadObjId);
                            }
                        }
                        else
                        {
                            character.SendMessage("[Doodad] /doodad pos <ObjId> <x> <y> <z> <rx> <ry> <rz> - Use x y z roll pitch yaw instead of a value to keep current position");
                        }
                    }
                    else
                    {
                        character.SendMessage("[Doodad] /doodad pos <ObjId> <x> <y> <z> <rx> <ry> <rz> - Use x y z roll pitch yaw instead of a value to keep current position");
                    }
                    break;
                case "save":
                    if (args.Length >= 2)
                    {
                        var spawners = new List<JsonDoodadSpawns>();
                        var doodadSpawners = new Dictionary<uint, JsonDoodadSpawns>();
                        // 0    1
                        // save objId
                        if (uint.TryParse(args[1], out doodadObjId))
                        {
                            doodad = WorldManager.Instance.GetDoodad(doodadObjId);
                            if ((doodad != null) && (doodad is Doodad))
                            {
                                // Save Doodad
                                try
                                {
                                    foreach (var world in worlds)
                                    {
                                        // Load Doodad spawns
                                        _log.Info("Loading spawns...");
                                        var contents = string.Empty;
                                        var worldPath = Path.Combine(FileManager.AppPath, "Data", "Worlds", world.Name);
                                        var jsonFileName = string.Empty;
                                        jsonFileName = Path.Combine(worldPath, "doodad_spawns_new.json");

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
                                                        doodadSpawners.Add(spawner.Id, spawner);
                                                    }
                                                }
                                                else
                                                    throw new Exception(string.Format("SpawnManager: Parse {0} file", jsonFileName));

                                                // добавим измененный Doodad
                                                doodad = WorldManager.Instance.GetDoodad(doodadObjId);
                                                if ((doodad != null) && (doodad is Doodad))
                                                {
                                                    var spawn = new JsonDoodadSpawns();

                                                    spawn.Position = new DoodadPos();

                                                    spawn.Id = doodad.ObjId;
                                                    spawn.UnitId = doodad.TemplateId;

                                                    spawn.Position.X = doodad.Transform.Local.Position.X;
                                                    spawn.Position.Y = doodad.Transform.Local.Position.Y;
                                                    spawn.Position.Z = doodad.Transform.Local.Position.Z;

                                                    spawn.Position.Roll = doodad.Transform.Local.Rotation.X.RadToDeg();
                                                    spawn.Position.Pitch = doodad.Transform.Local.Rotation.Y.RadToDeg();
                                                    spawn.Position.Yaw = doodad.Transform.Local.Rotation.Z.RadToDeg();

                                                    if (doodadSpawners.ContainsKey(spawn.Id))
                                                    {
                                                        doodadSpawners[spawn.Id] = spawn;
                                                    }
                                                    else
                                                    {
                                                        doodadSpawners.Add(spawn.Id, spawn);
                                                    }
                                                }

                                                var serialized = JsonConvert.SerializeObject(doodadSpawners.Values.ToArray(), Formatting.Indented);
                                                FileManager.SaveFile(serialized, string.Format(jsonFileName, FileManager.AppPath));
                                                character.SendMessage("[Doodad] Doodad ObjId: {0} has been saved!", doodad.ObjId);
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
                                character.SendMessage("|cFFFF0000[Doodad] Doodad with objId {0} Does not exist |r", doodadObjId);
                            }
                        }
                        else
                        {
                            character.SendMessage("[Doodad] /doodad save <ObjId>");
                        }
                    }
                    else
                    {
                        character.SendMessage("[Doodad] /doodad save <ObjId>");
                    }
                    break;
                default:
                    character.SendMessage("[Doodad] /doodad [chain <TemplateId>]||[setphase||save <ObjId>]||[pos <ObjId> <x> <y> <z> <rx> <ry> <rz>] - Use x y z roll pitch yaw instead of a value to keep current position");
                    break;
            }
        }

        private static void GoToPhaseChanged(Doodad doodad, uint funcGroupId)
        {
            doodad.FuncGroupId = funcGroupId;
            // Get all doodad_phase_funcs
            var phaseFuncs = DoodadManager.Instance.GetPhaseFunc(funcGroupId);
            foreach (var phaseFunc in phaseFuncs)
            {
                doodad.OverridePhase = 0;

                // phaseFunc.Use
                _log.Warn("[Doodad] PhaseFunc: GroupId {0}, FuncId {1}, FuncType {2}", phaseFunc.GroupId, phaseFunc.FuncId, phaseFunc.FuncType);

                if (doodad.OverridePhase > 0)
                {
                    doodad.FuncGroupId = (uint)doodad.OverridePhase;
                    GoToPhaseChanged(doodad, doodad.FuncGroupId);
                }
            }

            _log.Info("[Doodad] SCDoodadPhaseChangedPacket: Doodad {0}, templateId {1}", doodad.ObjId, doodad.TemplateId);
        }
    }
}
