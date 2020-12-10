using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Scripts.Commands
{
    public class Kick: ICommand
    {
        public void OnLoad()
        {
            string[] name = { "kick_player" };
            CommandManager.Instance.Register(name, this);
        }

        public string GetCommandLineHelp()
        {
            return "(character name || id) (reason) (msg)";
        }

        public string GetCommandHelpText()
        {
            return "Kicks target";
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length < 3)
            {
                character.SendMessage("[Kick] Usage : {0}", GetCommandLineHelp());
                return;
            }

            var targetChar = uint.TryParse(args[0], out uint characterId) ? WorldManager.Instance.GetCharacterById(characterId) : WorldManager.Instance.GetCharacter(args[0]);
            
            if (targetChar == null)
            {
                character.SendMessage("[Kick] Target not found");
                return;
            }
            
            var reason = (KickedReason)byte.Parse(args[1]);
            var msg = "";
            for (var x = 2; x < args.Length; x++)
                msg += args[x] + " ";

            targetChar.SendPacket(new SCKickedPacket(reason, msg));
        }
    }
}
