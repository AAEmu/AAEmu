using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;

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

            if ((character.LocalPingPosition.Position1[1].X == 0f) && (character.LocalPingPosition.Position1[1].Y == 0f))
            {
                character.SendMessage("|cFFFFFF00[PingPos] Make sure you marked a location on the map WHILE IN A PARTY OR RAID, using this command.\n" +
                    "If required, you can use the /soloparty command to make a party of just yourself.|r");
            }
            else
            {
                //var height = WorldManager.Instance.GetHeight(character.Position.ZoneId, character.LocalPingPosition.X, character.LocalPingPosition.Y);
                var height = character.LocalPingPosition.Position1[1].Z;
                if (height == 0f)
                {
                    character.SendMessage("|cFFFF0000[PingPos] |cFFFFFFFFX:" + character.LocalPingPosition.Position1[1].X.ToString("0.0") + " Y:" + character.LocalPingPosition.Position1[1].Y.ToString("0.0") + " Z: ???|r");
                }
                else
                {
                    character.SendMessage("|cFFFF0000[PingPos] |cFFFFFFFFX:" + character.LocalPingPosition.Position1[1].X.ToString("0.0") + " Y:" + character.LocalPingPosition.Position1[1].Y.ToString("0.0") + " Z:" + height.ToString("0.0") + "|r");
                }
            }

        }
    }
}
