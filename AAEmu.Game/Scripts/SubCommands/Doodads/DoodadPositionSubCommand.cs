using System.Collections.Generic;
using System.Drawing;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.SubCommands.Doodads;

public class DoodadPositionSubCommand : SubCommandBase
{
    public DoodadPositionSubCommand()
    {
        Title = "[Doodad Position]";
        Description = "Manage Doodad positioning in the world. All positions are optional use all or only the ones you want to change";
        CallPrefix = $"{CommandManager.CommandPrefix}doodad position||pos";
        AddParameter(new NumericSubCommandParameter<uint>("ObjId", "Object Id", true));
        AddParameter(new NumericSubCommandParameter<float>("x", "x=<new x>", false, "x"));
        AddParameter(new NumericSubCommandParameter<float>("y", "y=<new y>", false, "y"));
        AddParameter(new NumericSubCommandParameter<float>("z", "z=<new z>", false, "z"));
        AddParameter(new NumericSubCommandParameter<float>("roll", "roll=<new roll degrees>", false, "roll", -360f, 360f));
        AddParameter(new NumericSubCommandParameter<float>("pitch", "pitch=<new pitch degrees>", false, "pitch", -360f, 360f));
        AddParameter(new NumericSubCommandParameter<float>("yaw", "yaw=<new yaw degrees>", false, "yaw", -360f, 360f));
    }

    public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
    {
        uint doodadObjId = parameters["ObjId"];
        var doodad = WorldManager.Instance.GetDoodad(doodadObjId);
        if (doodad is null || !(doodad is Doodad))
        {
            SendColorMessage(messageOutput, Color.Red, $"Doodad with objId {doodadObjId} Does not exist");
            return;
        }

        var x = GetOptionalParameterValue(parameters, "x", doodad.Transform.Local.Position.X);
        var y = GetOptionalParameterValue(parameters, "y", doodad.Transform.Local.Position.Y);
        var z = GetOptionalParameterValue(parameters, "z", doodad.Transform.Local.Position.Z);
        var roll = GetOptionalParameterValue(parameters, "roll", doodad.Transform.Local.Rotation.X.RadToDeg()).DegToRad();
        var pitch = GetOptionalParameterValue(parameters, "pitch", doodad.Transform.Local.Rotation.Y.RadToDeg()).DegToRad();
        var yaw = GetOptionalParameterValue(parameters, "yaw", doodad.Transform.Local.Rotation.Z.RadToDeg()).DegToRad();

        SendMessage(messageOutput, $"Doodad ObjId: {doodad.ObjId} TemplateId:{doodad.TemplateId}, x:{x}, y:{y}, z:{z}, roll:{roll.RadToDeg():0.#}°, pitch:{pitch.RadToDeg():0.#}°, yaw:{yaw.RadToDeg():0.#}°");

        doodad.Transform.Local.SetPosition(x, y, z, roll, pitch, yaw);

        doodad.Hide();
        doodad.Show();
    }
}
