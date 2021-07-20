using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Core.Managers.World;

namespace AAEmu.Game.Scripts.Commands
{
    public class AddPortals : ICommand
    {
        public void OnLoad()
        {
            string[] name = { "addportal", "add_portal" };
            CommandManager.Instance.Register(name, this);
        }

        public string GetCommandLineHelp()
        {
            return "(target) <name> [<x> <y> <z> <zoneid>]";
        }

        public string GetCommandHelpText()
        {
            return "Adds a portal with <name> to your teleport book.\n" +
                "If [<x> <y> <z> <zoneid>] is ommited or incomplete, your current position will be used.";
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length == 0)
            {
                character.SendMessage("[Portal] " + CommandManager.CommandPrefix + "add_portal (target) <name> [<x> <y> <z> <zoneid>]");
                //character.SendMessage("[Portal] *optional (will get actual position)");
                return;
            }

            Character targetPlayer = WorldManager.Instance.GetTargetOrSelf(character, args[0], out var firstarg);

            var portalName = args[firstarg+0];
            var position = character.Transform.CloneAsSpawnPosition();
            var x = position.X;
            var y = position.Y;
            var z = position.Z;
            var zRot = position.Roll;
            var zoneId = position.ZoneId;

            if ((args.Length == firstarg + 5) && (float.TryParse(args[firstarg + 1], out float argx)))
                x = argx;
            if ((args.Length == firstarg + 5) && (float.TryParse(args[firstarg + 2], out float argy)))
                y = argy;
            if ((args.Length == firstarg + 5) && (float.TryParse(args[firstarg + 3], out float argz)))
                z = argz;
            if ((args.Length == firstarg + 5) && (uint.TryParse(args[firstarg + 4], out uint argzoneId)))
                zoneId = argzoneId;
            // If not using the current location, set the rotation to zero
            if (args.Length == firstarg + 5)
                zRot = 0 ;

            targetPlayer.Portals.AddPrivatePortal(x, y, z, zRot, zoneId, portalName);
            if (character.Id != targetPlayer.Id)
            {
                character.SendMessage("[Portal] added {0} labor to {1}'s portal book", portalName, targetPlayer.Name);
                targetPlayer.SendMessage("[GM] {0} has added the entry \"{1}\" to your portal book", character.Name, portalName);
            }
            else
            {
                character.SendMessage("[Portal] Registered \"{0}\" in your portal book", portalName);
            }
        }
    }
}
