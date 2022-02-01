using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Skills;
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
            Doodad doodad;
            uint skillId = 0;

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
                                if (doodadFuncGroups.Count <= 0)
                                {
                                    return;
                                }
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

                                            if (doodad.OverridePhase > 0)
                                            {
                                                doodad.FuncGroupId = doodad.OverridePhase;
                                                GoToPhaseChanged(doodad, doodad.FuncGroupId);
                                            }
                                        }
                                        if (doodad.FuncGroupId == (uint)func.NextPhase || func.NextPhase == -1)
                                            return; // закончим цикл функций, так как вернулись к первой или функций нет
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
                default:
                    character.SendMessage("[Dooda] /doodad_chain list <templateId>");
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
