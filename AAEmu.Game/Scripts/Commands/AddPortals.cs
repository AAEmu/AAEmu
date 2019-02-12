using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Scripts.Commands
{
    public class AddPortals : ICommand
    {
        public void OnLoad()
        {
            CommandManager.Instance.Register("add_portal", this);
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length == 0)
            {
                character.SendMessage("[AddPortal] /add_portal <name> <x>* <y>* <z>* <zoneid>*");
                character.SendMessage("[AddPortal] *optional (will get actual position)");
                return;
            }

            var portalName = args[0];
            var position = character.Position;
            var x = args.Length == 5 ? float.Parse(args[1]) : position.X;
            var y = args.Length == 5 ? float.Parse(args[2]) : position.Y;
            var z = args.Length == 5 ? float.Parse(args[3]) : position.Z;
            var zoneId = args.Length == 5 ? uint.Parse(args[4]) : position.ZoneId;

            character.Portals.AddPrivatePortal(x, y, z, zoneId, portalName);
            character.SendMessage("[AddPortal] Success");
        }
    }
}
