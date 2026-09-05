using System.Drawing;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.SubCommands.Doodads;

public class DoodadSetAgeCommand : SubCommandBase
{
    public DoodadSetAgeCommand()
    {
        Title = "[Doodad SetAge]";
        Description = "Changes a doodads age in seconds (1h = 3600, 1d = 86400)";
        CallPrefix = $"{CommandManager.CommandPrefix}doodad age||setage";
        AddParameter(new NumericSubCommandParameter<uint>("ObjId", "Object Id", true));
        AddParameter(new NumericSubCommandParameter<uint>("Age", "New Age Time", true));
    }

    public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
    {
        uint doodadObjId = parameters["ObjId"];
        uint newAge = parameters["Age"];
        var doodad = ((Character)character).ParentWorld.GetDoodad(doodadObjId);
        if (doodad is null)
        {
            SendColorMessage(messageOutput, Color.Red, $"Doodad with objId {doodadObjId} Does not exist");
            return;
        }

        SendMessage(messageOutput, $"Doodad ObjId: {doodad.ObjId} TemplateId:{doodad.TemplateId}, set new plat time to {newAge} seconds ago.");

        doodad.PlantTime = DateTime.UtcNow.AddSeconds(-newAge);

        doodad.Hide();
        doodad.Show();
    }
}
