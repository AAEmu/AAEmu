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
            if (args.Length < 2)
            {
                character.SendMessage("[Items] /add_portal <id> <name> <houseBook>* <x>* <y>* <z>*");
                character.SendMessage("[Items] *optional / houseBook is Boolean");
                return;
            }
            var portalId = uint.Parse(args[0]);
            var portalName = args[1];
            var position = character.Position;
            var whatBook = args.Length > 2 ? bool.Parse(args[2]) : true;

            var portal = new Portal();
            portal.Id = portalId;
            portal.Name = portalName;
            portal.ZoneId = position.ZoneId;

            portal.X = args.Length >= 6 ? float.Parse(args[3]) : position.X;
            portal.Y = args.Length >= 6 ? float.Parse(args[4]) : position.Y;
            portal.Z = args.Length >= 6 ? float.Parse(args[5]) : position.Z;

            portal.ZRot = 0;

            if (whatBook)
                character.SendPacket(new SCCharacterPortalsPacket(new Portal[] { portal }));
            else
            {
                character.SendPacket(new SCCharacterReturnDistrictsPacket(new Portal[] { portal }, 468));
            }
        }
    }
}
