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
            ///register script
            CommandManager.Instance.Register("snow", this);
        }

        public string GetCommandLineHelp()
        {
            return "<true||false>";
        }

        public string GetCommandHelpText()
        {
            return "Enables or disables snow effect across the server";
        }

        public void Execute(Character character, string[] args)
        {
            // If no argument is provided send usage information
            if (args.Length == 0)
            {
                character.SendMessage("[Snow] " + CommandManager.CommandPrefix + "snow <true/false>");
                return;
            }

            // determine if we recived true,false or something else             
            if (bool.TryParse(args[0], out var isSnowing))
            {
                //Set Snowing state to user input, This will 
                // enable Snow on all players who login to the server
                WorldManager.Instance.IsSnowing = isSnowing;

                //Turn snow on or off for all online characters,
                //put this on the script level so it only gets executed once when GM enables/disables snow
                WorldManager.Instance.BroadcastPacketToServer(new SCOnOffSnowPacket(isSnowing));
            }
            else
            {
                // user input was invalid notify them
                character.SendMessage("[Snow] Use true or false.");
            }


        }
    }
}
