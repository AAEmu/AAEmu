using System;
using System.Drawing;
using System.Linq;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Char;
using NLog;

namespace AAEmu.Game.Utils.Scripts.SubCommands
{
    public class DoodadChainSubCommand : SubCommandBase
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        public DoodadChainSubCommand()
        {
            Prefix = "[Doodad Chain]";
            Description = "Show all subrelated properties of a Doodad";
            CallExample = "/doodad chain <templateId>";
        }

        public override void Execute(ICharacter character, string triggerArgument, string[] args)
        {
            var firstArgument = args.FirstOrDefault();

            if (firstArgument is null) 
            { 
                SendMessage(character,"/doodad chain <templateId>");
                return;
            }

            if (!uint.TryParse(args[1], out var templateId))
            {
                SendColorMessage(character, Color.Red, "Invalid templateId, must be numeric");
                return;
            }

            var doodad = DoodadManager.Instance.Create(0, templateId);
            if (doodad == null)
            {
                SendColorMessage(character, Color.Red, "Doodad with templateId {0} was not found |r", templateId);
                return;
            }

            SendMessage(character, "Phase chain, see the log");
            _log.Warn($"{Prefix} Chain: TemplateId {templateId}");
            try
            {
                var doodadFuncGroups = DoodadManager.Instance.GetDoodadFuncGroups(templateId);
                foreach (var doodadFuncGroup in doodadFuncGroups)
                {
                    // Display all functions that are available
                    doodad.FuncGroupId = doodadFuncGroup.Id;
                    _log.Info($"{Prefix} FuncGroupId: {doodad.FuncGroupId}");
                    // Get all doodad_phase_funcs
                    var phaseFuncs = DoodadManager.Instance.GetPhaseFunc(doodad.FuncGroupId);
                    if (phaseFuncs.Length == 0)
                    {
                        _log.Info($"{Prefix} PhaseFunc: GroupId {0}, FuncId 0", doodad.FuncGroupId);
                    }
                    else
                    {
                        foreach (var phaseFunc in phaseFuncs)
                        {
                            // phaseFunc.Use
                            _log.Info($"{Prefix} PhaseFunc: GroupId {0}, FuncId {1}, FuncType {2}", phaseFunc.GroupId, phaseFunc.FuncId, phaseFunc.FuncType);
                        }
                    }
                    // Get all doodad_funcs
                    var doodadFuncs = DoodadManager.Instance.GetDoodadFuncs(doodad.FuncGroupId);
                    if (doodadFuncs.Count == 0)
                    {
                        _log.Info($"{Prefix} Func: GroupId {0}, FuncId 0", doodad.FuncGroupId);
                    }
                    else
                    {
                        foreach (var func in doodadFuncs)
                        {
                            // func.Use
                            _log.Info($"{Prefix} Func: GroupId {0}, FuncId {1}, FuncType {2}, NextPhase {3}, Skill {4}", func.GroupId, func.FuncId, func.FuncType, func.NextPhase, func.SkillId);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                character.SendMessage(Color.Red, e.Message);
                _log.Fatal(e, $"{Prefix} Doodad func crashed !");
            }
        }
    }
}
