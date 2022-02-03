using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Stream;

using NLog;

using static AAEmu.Game.Models.Game.DoodadObj.DoodadFuncGroups;

namespace AAEmu.Game.Utils
{
    public class DoodadCommandUtil
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        public static void GetCommandChoice(Character character, string choice, string[] args)
        {
            uint templateId;
            uint doodadObjId;
            Doodad doodad;
            uint skillId = 0;
            uint phase = 0;

            switch (choice)
            {
                case "list":
                    if (args.Length >= 2)
                    {
                        if (uint.TryParse(args[1], out templateId))
                        {
                            doodad = DoodadManager.Instance.Create(0, templateId);

                            character.SendMessage("[Doodad] LIST");
                            _log.Warn("[Doodad] Chain: TemplateId {0}", templateId);
                            try
                            {
                                var doodadFuncGroups = DoodadManager.Instance.GetDoodadFuncGroups(templateId);
                                foreach (var doodadFuncGroup in doodadFuncGroups)
                                {
                                    // Display all functions that are available
                                    doodad.FuncGroupId = doodadFuncGroup.Id;
                                    _log.Info("[Doodad] FuncGroupId : {0}", doodad.FuncGroupId);
                                    // Get all doodad_funcs
                                    var doodadFuncs = DoodadManager.Instance.GetDoodadFuncs(doodad.FuncGroupId);
                                    foreach (var func in doodadFuncs)
                                    {
                                        // func.Use
                                        _log.Info("[Doodad] Func: GroupId {0}, FuncId {1}, FuncType {2}, NextPhase {3}, Skill {4}", func.GroupId, func.FuncId, func.FuncType, func.NextPhase, func.SkillId);
                                        // Get all doodad_phase_funcs
                                        var phaseFuncs = DoodadManager.Instance.GetPhaseFunc((uint)func.NextPhase);
                                        foreach (var phaseFunc in phaseFuncs)
                                        {
                                            doodad.OverridePhase = 0;
                                            // phaseFunc.Use
                                            _log.Info("[Doodad] PhaseFunc: GroupId {0}, FuncId {1}, FuncType {2}, NextPhase {3}, Skill {4}", phaseFunc.GroupId, phaseFunc.FuncId, phaseFunc.FuncType, phaseFunc.NextPhase, phaseFunc.SkillId);
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
                        character.SendMessage("[Doodad] /doodad_chain list <templateId>");
                    }
                    break;
                case "setphase":
                    if (args.Length >= 3)
                    {
                        if (uint.TryParse(args[1], out doodadObjId))
                        {
                            if (uint.TryParse(args[2], out phase))
                            {
                                doodad = WorldManager.Instance.GetDoodad(doodadObjId);
                                if ((doodad != null) && (doodad is Doodad))
                                {
                                    var listIds = DoodadManager.Instance.GetDoodadFuncGroupsId(doodad.TemplateId);
                                    character.SendMessage("[Doodad] SetPhase {0}", phase);
                                    character.SendMessage("[Doodad] TemplateId {0}: ObjId{1}, Phases({2})", doodad.TemplateId, doodad.ObjId, string.Join(", ", listIds));
                                    _log.Warn("[Doodad] Chain: TemplateId {0}, doodadObjId {1}", doodad.TemplateId, doodad.ObjId);
                                    doodad.DoPhaseFuncs(character, (int)phase);
                                }
                                else
                                {
                                    character.SendMessage("|cFFFF0000[Spawn] Doodad with Id {0} Does not exist |r", doodadObjId);
                                }
                            }
                        }
                        else
                        {
                            character.SendMessage("[Doodad] /doodad_chain setphase <ObjId> <Phase>");
                        }
                    }
                    else
                    {
                        character.SendMessage("[Doodad] /doodad_chain setphase <ObjId> <Phase>");
                    }
                    break;
                default:
                    character.SendMessage("[Dooda] /doodad_chain <list|setphase>");
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
                _log.Warn("[Doodad] PhaseFunc: GroupId {0}, FuncId {1}, FuncType {2}, NextPhase {3}, Skill {4}", phaseFunc.GroupId, phaseFunc.FuncId, phaseFunc.FuncType, phaseFunc.NextPhase, phaseFunc.SkillId);

                if (doodad.OverridePhase > 0)
                {
                    doodad.FuncGroupId = doodad.OverridePhase;
                    GoToPhaseChanged(doodad, doodad.FuncGroupId);
                }
            }

            _log.Info("[Doodad] SCDoodadPhaseChangedPacket: Doodad {0}, templateId {1}", doodad.ObjId, doodad.TemplateId);
        }
    }
}
