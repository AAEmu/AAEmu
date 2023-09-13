using System.Collections.Generic;
using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.Commands
{
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
            AddParameter(new NumericSubCommandParameter<float>("roll", "roll=<new roll degrees>", false, "roll", 0, 360));
            AddParameter(new NumericSubCommandParameter<float>("pitch", "pitch=<new pitch degrees>", false, "pitch", 0, 360));
            AddParameter(new NumericSubCommandParameter<float>("yaw", "yaw=<new yaw degrees>", false, "yaw", 0, 360));
        }

        public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters)
        {
            uint doodadObjId = parameters["ObjId"];
            var doodad = WorldManager.Instance.GetDoodad(doodadObjId);
            if (doodad is null || !(doodad is Doodad))
            {
                SendColorMessage(character, Color.Red, $"Doodad with objId {doodadObjId} Does not exist |r");
                return;
            }

            float x = GetOptionalParameterValue(parameters, "x", doodad.Transform.Local.Position.X);
            float y = GetOptionalParameterValue(parameters, "y", doodad.Transform.Local.Position.Y);
            float z = GetOptionalParameterValue(parameters, "z", doodad.Transform.Local.Position.Z);
            var roll = GetOptionalParameterValue(parameters, "roll", doodad.Transform.Local.Rotation.X.RadToDeg()).DegToRad();
            var pitch = GetOptionalParameterValue(parameters, "pitch", doodad.Transform.Local.Rotation.Y.RadToDeg()).DegToRad();
            var yaw = GetOptionalParameterValue(parameters, "yaw", doodad.Transform.Local.Rotation.Z.RadToDeg()).DegToRad();

            SendMessage(character, "Doodad ObjId: {0} TemplateId:{1}, x:{2}, y:{3}, z:{4}, roll:{5:0.#}°, pitch:{6:0.#}°, yaw:{7:0.#}°",
                doodad.ObjId, doodad.TemplateId, x, y, z, roll.RadToDeg(), pitch.RadToDeg(), yaw.RadToDeg());

            doodad.Transform.Local.SetPosition(x, y, z, roll, pitch, yaw);

            doodad.Hide();
            doodad.Show();
        }
    }
}
