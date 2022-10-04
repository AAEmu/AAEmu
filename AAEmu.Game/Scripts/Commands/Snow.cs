using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Scripts.Commands
{
    public class Snow : ICommand
    {
        public void OnLoad()
        {
            CommandManager.Instance.Register("snow", this);
        }

        public string GetCommandLineHelp()
        {
            return "<true||false>";
        }

        public string GetCommandHelpText()
        {
            return "Enables or disables snow effect across the server.";
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length == 0)
            {
                character.SendMessage($"[Snow] {CommandManager.CommandPrefix}snow <true/false>");
                return;
            }

            if (bool.TryParse(args[0], out var isSnowing))
            {
                // enable Snow on all players who login to the server
                WorldManager.Instance.IsSnowing = isSnowing;
                // Turn snow on or off for all online characters
                WorldManager.Instance.BroadcastPacketToServer(new SCOnOffSnowPacket(isSnowing));
                return;
            }

            character.SendMessage($"[Snow] Couldn't parse {args[0]} as true/false");
        }
    }
}
