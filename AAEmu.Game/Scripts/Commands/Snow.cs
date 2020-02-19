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
       
        public void Execute(Character character, string[] args)
        {
            // If no argument is provided send usage information
            if (args.Length == 0)
            {
                character.SendMessage("[Snow] /snow <true/false>");
                return;
            }

            // determine if we recived true,false or something else if true or false            
            if (bool.TryParse(args[0], out var isFlying))
            {
                //call worldmanager and broadcast snow packet to all online chars
                WorldManager.Instance.BroadcastPacketToServer(new SCOnOffSnowPacket(isFlying));
            }
            else
            {
                // user input was invalid notify them
                character.SendMessage("[Snow] Use true or false.");
            }
               

        }
    }
}
