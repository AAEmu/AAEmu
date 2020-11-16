

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Scripts.Commands
{
    public class Online : ICommand
    {
        public void OnLoad()
        {
            string[] name = { "online", "list_online" };
            CommandManager.Instance.Register(name, this);
        }

        public string GetCommandLineHelp()
        {
            return "";
        }

        public string GetCommandHelpText()
        {
            return "Lists all online players";
        }

        public void Execute(Character character, string[] args)
        {
            var characters = WorldManager.Instance.GetAllCharacters();
            var finalMessage = characters.Count + " players online. |cFFFFFFFF";
            foreach (var onlineCharacter in characters)
            {
                finalMessage += (onlineCharacter.Name + "|r,|cFFFFFFFF ");
            }

            finalMessage += "|r";

            character.SendMessage(finalMessage);
        }
    }
}
