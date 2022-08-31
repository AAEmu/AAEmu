using System.Collections.Generic;
using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.Commands
{
    public class DoodadChainSubCommand : SubCommandBase
    {
        public DoodadChainSubCommand()
        {
            Title = "[Doodad Chain]";
            Description = "Show all subrelated properties of a Doodad";
            CallPrefix = $"{CommandManager.CommandPrefix}doodad chain";
            AddParameter(new NumericSubCommandParameter<uint>("templateId", "Template Id", true));
        }

        public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters)
        {
            uint templateId = parameters["templateId"];
            var doodad = DoodadManager.Instance.Create(0, templateId);
            if (doodad == null)
            {
                SendColorMessage(character, Color.Red, "Doodad with templateId {0} was not found |r", templateId);
                return;
            }

            SendMessage(character, "Phase chain, see the log");
            _log.Warn($"{Title} Chain: TemplateId {templateId}");
            
            var doodadFuncGroups = DoodadManager.Instance.GetDoodadFuncGroups(templateId);
            foreach (var doodadFuncGroup in doodadFuncGroups)
            {
                // Display all functions that are available
                doodad.FuncGroupId = doodadFuncGroup.Id;
                _log.Info($"{Title} FuncGroupId: {doodad.FuncGroupId}");
                // Get all doodad_phase_funcs
                var phaseFuncs = DoodadManager.Instance.GetPhaseFunc(doodad.FuncGroupId);
                if (phaseFuncs.Length == 0)
                {
                    _log.Info($"{Title} PhaseFunc: GroupId {0}, FuncId 0", doodad.FuncGroupId);
                }
                else
                {
                    foreach (var phaseFunc in phaseFuncs)
                    {
                        // phaseFunc.Use
                        _log.Info($"{Title} PhaseFunc: GroupId {0}, FuncId {1}, FuncType {2}", phaseFunc.GroupId, phaseFunc.FuncId, phaseFunc.FuncType);
                    }
                }
                // Get all doodad_funcs
                var doodadFuncs = DoodadManager.Instance.GetDoodadFuncs(doodad.FuncGroupId);
                if (doodadFuncs.Count == 0)
                {
                    _log.Info($"{Title} Func: GroupId {0}, FuncId 0", doodad.FuncGroupId);
                }
                else
                {
                    foreach (var func in doodadFuncs)
                    {
                        // func.Use
                        _log.Info($"{Title} Func: GroupId {0}, FuncId {1}, FuncType {2}, NextPhase {3}, Skill {4}", func.GroupId, func.FuncId, func.FuncType, func.NextPhase, func.SkillId);
                    }
                }
            }
        }
    }
}
