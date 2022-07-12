using System.Drawing;
using System.Linq;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Utils.Scripts.SubCommands
{
    public class DoodadPositionSubCommand : SubCommandBase
    {
        public DoodadPositionSubCommand()
        {
            Title = "[Doodad Position]";
            Description = "Manage Doodad positioning in the world.";
            CallPrefix = "/doodad pos <ObjId> x=<x> y=<y> z=<z> roll=<roll> pitch=<pitch> yaw=<yaw> - All positions are optional use all or only the ones you want to change";
        }

        public override void Execute(ICharacter character, string triggerArgument, string[] args)
        {
            var firstParameter = args.FirstOrDefault();
            if (firstParameter is null)
            {
                SendMessage(character, "Doodad <ObjId> parameter missing");
                return;
            }

            if (!uint.TryParse(firstParameter, out var doodadObjId))
            {
                SendMessage(character, "Doodad <ObjId> parameter should be a number");
                return;
            }

            
            var doodad = WorldManager.Instance.GetDoodad(doodadObjId);
            if (doodad is null || !(doodad is Doodad))
            {
                SendColorMessage(character, Color.Red, $"Doodad with objId {doodadObjId} Does not exist |r");
                return;
            }

            float x = GetOptionalArgumentValue(args, "x", doodad.Transform.Local.Position.X);
            float y = GetOptionalArgumentValue(args, "y", doodad.Transform.Local.Position.Y);
            float z = GetOptionalArgumentValue(args, "z", doodad.Transform.Local.Position.Z);
            var roll = GetOptionalArgumentValue(args, "roll", doodad.Transform.Local.Rotation.X.RadToDeg()).DegToRad();
            var pitch = GetOptionalArgumentValue(args, "pitch", doodad.Transform.Local.Rotation.Y.RadToDeg()).DegToRad();
            var yaw = GetOptionalArgumentValue(args, "yaw", doodad.Transform.Local.Rotation.Z.RadToDeg()).DegToRad();


            SendMessage(character,"Doodad ObjId: {0} TemplateId:{1}, x:{2}, y:{3}, z:{4}, roll:{5}, pitch:{6}, yaw:{7}",
                doodad.ObjId, doodad.TemplateId, x, y, z, roll.RadToDeg(), pitch.RadToDeg(), yaw.RadToDeg());

            doodad.Transform.Local.SetPosition(x, y, z, roll, pitch, yaw);

            doodad.Hide();
            doodad.Show();
        }
    }
}
