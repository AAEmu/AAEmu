using System.Drawing;
using System.Linq;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Utils.Scripts.SubCommands
{
    public class DoodadSpawnSubCommand : SubCommandBase
    {
        public DoodadSpawnSubCommand()
        {
            Prefix = "[Doodad Spawn]";
            Description = "Add a new doodad of a specific template 3 meters in front of the player.";
            CallExample = "/doodad spawn <TemplateId> [yaw=<yaw>] - Optional yaw(rotation) angle. Default will use characters facing angle.";
        }

        public override void Execute(ICharacter character, string triggerArgument, string[] args)
        {
            var firstArgument = args.FirstOrDefault();
            if (firstArgument is null)
            {
                SendMessage(character, "/doodad spawn <TemplateId> [yaw=<yaw>] - Optional yaw(rotation) angle. Default will use characters facing angle.");
                return;
            }

            if (!uint.TryParse(firstArgument, out var unitTemplateId))
            {
                SendColorMessage(character, Color.Red, "Invalid <TemplateId>, must be numeric");
                return;
            }

            var charPos = ((Character)character).Transform.CloneDetached();
            charPos.Local.AddDistanceToFront(3f);
            var defaultYaw = (float)MathUtil.CalculateAngleFrom(charPos, ((Character)character).Transform);
            var newYaw = GetOptionalArgumentValue(args, "yaw", defaultYaw.RadToDeg()).DegToRad();

            if (!DoodadManager.Instance.Exist(unitTemplateId))
            {
                SendColorMessage(character, Color.Red, "Doodad templateId:{0} don't exist|r", unitTemplateId);
                return;
            }

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

            if (args.Length > 1)
            {
                character.SendMessage("Doodad ObjId:{0}, Template:{0} spawned using yaw {1}° = {2} rad", createdDoodad.ObjId, unitTemplateId, newYaw.RadToDeg(), newYaw);
            }
            else
            {
                character.SendMessage("Doodad ObjId:{0}, Template {0} spawned facing you, characters yaw {1}°", createdDoodad.ObjId, unitTemplateId, newYaw.RadToDeg());
            }
        }
    }
}
