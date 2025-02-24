using System.Collections.Generic;
using System.Drawing;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.SubCommands.Doodads;

public class DoodadPhaseListSubCommand : SubCommandBase
{
    public DoodadPhaseListSubCommand()
    {
        Title = "[Doodad Phase List]";
        Description = "List all the phases of a given doodad";
        CallPrefix = $"{CommandManager.CommandPrefix}doodad phase list";
        AddParameter(new NumericSubCommandParameter<uint>("ObjId", "Object Id", true));
    }

    public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
    {
        uint doodadObjId = parameters["ObjId"];
        var doodad = WorldManager.Instance.GetDoodad(doodadObjId);
        if (doodad is null)
        {
            SendColorMessage(messageOutput, Color.Red, $"Doodad with objId {doodadObjId} Does not exist");
        }

        if (!(doodad is Doodad))
        {
            SendColorMessage(messageOutput, Color.Red, $"Doodad with objId {doodadObjId} is invalid (not a Doodad)");
        }

        var availablePhases = string.Join(", ", DoodadManager.Instance.GetDoodadFuncGroupsId(doodad.TemplateId));

        SendMessage(messageOutput, $"TemplateId {doodad.TemplateId}: ObjId{doodad.ObjId}, Available phase ids (func groups): {availablePhases}");
        Logger.Warn($"{Title} Chain: TemplateId {doodad.TemplateId}, doodadObjId {doodad.ObjId},  Available phase ids (func groups): {availablePhases}");
    }
}
