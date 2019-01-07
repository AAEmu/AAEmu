using System.Text;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Scripts.Commands
{
    public class Help : ICommand
    {
        public void OnLoad()
        {
            CommandManager.Instance.Register("help", this);
        }

        public void Execute(Character character, string[] args)
        {
            var list = CommandManager.Instance.GetCommandKeys();
            var sb = new StringBuilder();
            sb.AppendLine("Help List:");
            foreach (var command in list)
            {
                if (command == "help")
                    continue;
                sb.AppendLine($"/{command}");
            }

            character.SendMessage(sb.ToString());
        }
    }
}