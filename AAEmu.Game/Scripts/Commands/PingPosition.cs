using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Managers.World;

namespace AAEmu.Game.Scripts.Commands
{
    public class PingPosition : ICommand
    {
        public void OnLoad()
        {
            string[] names = { "pingpos", "ping_pos", "pingposition" };
            CommandManager.Instance.Register(names, this);
        }

        public string GetCommandLineHelp()
        {
            return "";
        }

        public string GetCommandHelpText()
        {
            return "Displays information about your pinged position. (map marker)";
        }

        public void Execute(Character character, string[] args)
        {
            var pos = character.LocalPingPosition;
            if ((pos.X == 0.0f) && (pos.Y == 0.0f))
            {
                character.SendMessage("|cFFFFFF00[PingPos] Must mark a location on the map WHILE IN A PARTY OR RAID before using this command.\n"
                                    + "See also, /soloparty command to make a solo party.|r");
            }
            else
            {
                var height = WorldManager.Instance.GetHeight(character.Transform.ZoneId, pos.X, pos.Y);
                if (height == 0.0f)
                {
                    character.SendMessage($"|cFFFF0000[PingPos] |cFFFFFFFFX:{pos.X.ToString("0.0")} Y:{pos.Y.ToString("0.0")} Z: ???|r");
                }
                else
                {
                    character.SendMessage($"|cFFFF0000[PingPos] |cFFFFFFFFX:{pos.X.ToString("0.0")} Y:{pos.Y.ToString("0.0")} Z:{height.ToString("0.0")}|r");
                }
            }
        }
    }
}
