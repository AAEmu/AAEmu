using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Scripts.Commands
{
    public class MoveAll : ICommand
    {
        public void Execute(Character character, string[] args)
        {
            foreach (var otherChar in WorldManager.Instance.GetAllCharacters())
            {
                if (otherChar != character)
                {
                    otherChar.DisabledSetPosition = true;
                    otherChar.SendPacket(new SCTeleportUnitPacket(0, 0, character.Position.X, character.Position.Y, character.Position.Z + 1.0f, 0f));
                }
            }
        }

        public void OnLoad()
        {
            string[] name = { "move_all", "moveall" };
            CommandManager.Instance.Register(name, this);
        }

        public string GetCommandLineHelp()
        {
            return "";
        }

        public string GetCommandHelpText()
        {
            return "Moves every player to your location";
        }
    }
}
