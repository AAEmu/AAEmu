using System.Collections.Generic;
using System.Drawing;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Utils.Scripts.SubCommands
{
    public class DoodadSpawnSubCommand : SubCommandBase
    {
        public DoodadSpawnSubCommand()
        {
            Title = "[Doodad Spawn]";
            Description = "Add a new doodad of a specific template 3 meters in front of the player. Default yaw will use characters facing angle.";
            CallPrefix = "/doodad spawn";
            AddParameter(new NumericSubCommandParameter<uint>("templateId", "Template Id", true));
            AddParameter(new NumericSubCommandParameter<float>("yaw", "yaw=<yaw facing degrees>", false, "yaw"));
        }

        public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters)
        {
            uint unitTemplateId = parameters["templateId"];
            if (!DoodadManager.Instance.Exist(unitTemplateId))
            {
                SendColorMessage(character, Color.Red, "Doodad templateId:{0} don't exist|r", unitTemplateId);
                return;
            }

            var charPos = ((Character)character).Transform.CloneDetached();
            charPos.Local.AddDistanceToFront(3f);
            var defaultYaw = (float)MathUtil.CalculateAngleFrom(charPos, ((Character)character).Transform);
            var newYaw = GetOptionalParameterValue(parameters, "yaw", defaultYaw.RadToDeg()).DegToRad();
            var doodadSpawner = new DoodadSpawner
            {
                Id = 0,
                UnitId = unitTemplateId
            };

            doodadSpawner.Position = charPos.CloneAsSpawnPosition();
            doodadSpawner.Position.Yaw = newYaw;
            doodadSpawner.Position.Pitch = 0;
            doodadSpawner.Position.Roll = 0;
            var createdDoodad = doodadSpawner.Spawn(0, 0, ((Character)character).ObjId);

            if (parameters.ContainsKey("yaw"))
            {
                character.SendMessage("Doodad ObjId:{0}, Template:{0} spawned using yaw {1:0.#}° = {2} rad", createdDoodad.ObjId, unitTemplateId, newYaw.RadToDeg(), newYaw);
            }
            else
            {
                character.SendMessage("Doodad ObjId:{0}, Template {0} spawned facing you, characters yaw {1:0.#}°", createdDoodad.ObjId, unitTemplateId, newYaw.RadToDeg());
            }
        }
    }
}
