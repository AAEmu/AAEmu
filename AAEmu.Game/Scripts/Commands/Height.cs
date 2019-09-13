using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Scripts.Commands
{
    public class Height : ICommand
    {
        public void OnLoad()
        {
            CommandManager.Instance.Register("height", this);
        }

        public string GetCommandLineHelp()
        {
            return "";
        }

        public string GetCommandHelpText()
        {
            return "Gets your current height and that of the supposed floor (using heightmap data)";
        }

        public void Execute(Character character, string[] args)
        {
            var height = WorldManager.Instance.GetHeight(character.Position.ZoneId, character.Position.X, character.Position.Y);
            character.SendMessage("Me: {0} - Floor: {1}", character.Position.Z, height);
        }
    }
}
