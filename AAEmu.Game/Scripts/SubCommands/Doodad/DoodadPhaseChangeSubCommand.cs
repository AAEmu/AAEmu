using System.Collections.Generic;
using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.Commands
{
    public class DoodadPhaseChangeSubCommand : SubCommandBase
    {
        public DoodadPhaseChangeSubCommand()
        {
            Title = "[Doodad Phase Change]";
            Description = "Change the phase of a given doodad";
            CallPrefix = $"{CommandManager.CommandPrefix}doodad phase change";
            AddParameter(new NumericSubCommandParameter<uint>("ObjId", "Object Id", true));
            AddParameter(new NumericSubCommandParameter<int>("PhaseId", "Phase Id", true));
        }
        public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters)
        {
            uint doodadObjId = parameters["ObjId"];
            int phaseId = parameters["PhaseId"];
            var doodad = WorldManager.Instance.GetDoodad(doodadObjId);
            if (doodad is null)
            {
                SendColorMessage(character, Color.Red, "Doodad with objId {0} Does not exist |r", doodadObjId);
            }
            if (!(doodad is Doodad))
            {
                SendColorMessage(character, Color.Red, "Doodad with objId {0} is invalid (not a Doodad) |r", doodadObjId);
            }

            var availablePhases = string.Join(", ", DoodadManager.Instance.GetDoodadFuncGroupsId(doodad.TemplateId));

            SendMessage(character, "SetPhase {0}", phaseId);
            SendMessage(character, "TemplateId {0}: ObjId:{1}, ChangedPhase:{2}, Available phase ids (func groups): {3}", doodad.TemplateId, doodad.ObjId, phaseId, availablePhases);
            _log.Warn($"{Title} Chain: TemplateId {doodad.TemplateId}, doodadObjId {doodad.ObjId}, SetPhase {phaseId}, Available phase ids (func groups): {availablePhases}");
            doodad.DoPhaseFuncs((Unit)character, phaseId);
        }
    }
}
