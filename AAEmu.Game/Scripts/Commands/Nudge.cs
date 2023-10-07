using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Teleport;

namespace AAEmu.Game.Scripts.Commands
{
    public class Nudge : ICommand
    {
        public void OnLoad()
        {
            CommandManager.Instance.Register("nudge", this);
        }

        public string GetCommandLineHelp()
        {
            return "(distance)";
        }

        public string GetCommandHelpText()
        {
            return "Move yourself forward by a given distance (of 5m by default).\n" +
                "Examples:\n" +
                CommandManager.CommandPrefix + "nudge\n" +
                CommandManager.CommandPrefix + "nudge 10";
        }

        public void Execute(Character character, string[] args, IMessageOutput messageOutput)
        {
			float dist = 5f;
            if ((args.Length > 1) && (!float.TryParse(args[0], out dist)))
            {
                character.SendMessage("|cFFFF0000[Nudge] Distance parse error|r");
                return;
            }
			
			character.DisabledSetPosition = true;
			// character.SendMessage("|cFFFF0000[Nudge] from {0}, {1}, {2}|r", character.Transform.World.Position.X, character.Transform.World.Position.Y, character.Transform.World.Position.Z);
			character.Transform.Local.AddDistanceToFront(dist, false);
            character.Transform.FinalizeTransform();
			// character.SendMessage("|cFFFF0000[Nudge] from {0}, {1}, {2}|r", character.Transform.World.Position.X, character.Transform.World.Position.Y, character.Transform.World.Position.Z);
            character.SendPacket(new SCTeleportUnitPacket(TeleportReason.Gm, 0, character.Transform.World.Position.X, character.Transform.World.Position.Y, character.Transform.World.Position.Z, character.Transform.World.Rotation.Z));
        }
    }
}
