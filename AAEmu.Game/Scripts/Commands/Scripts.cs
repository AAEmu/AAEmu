using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands
{
    public class Scripts : ICommand
    {
        public void OnLoad()
        {
            CommandManager.Instance.Register("scripts", this);
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length == 0)
            {
                character.SendMessage("[Scripts] Using: /scripts <action>");
                character.SendMessage("[Scripts] Action: reboot");
                return;
            }

            switch (args[0])
            {
                case "reboot":
                    CommandManager.Instance.Clear();
                    ScriptCompiler.Compile();
                    character.SendMessage("[Scripts] Done");
                    break;
                default:
                    character.SendMessage("[Scripts] Undefined action...");
                    break;
            }
        }
    }
}