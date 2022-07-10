using System;
using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.Commands
{
    public class GoldCmd : SubCommandBase, ICommand, ISubCommand
    {
        public GoldCmd()
        {
            Prefix = "[Gold]";
            Description = "Root command to manage gold";
            CallExample = "/gold (add||change||remove) ...";

            Register(new GoldChangeSubCommand(), "add");
            Register(new GoldChangeSubCommand(), "change");
            Register(new GoldChangeSubCommand(), "remove");
        }
        public void OnLoad()
        {
            CommandManager.Instance.Register("gold", this);
        }

        public string GetCommandLineHelp()
        {
            return $"<{string.Join("||", SupportedCommands)}>";
        }

        public string GetCommandHelpText()
        {
            return CallExample;
        }

        public void Execute(Character character, string[] args)
        {
            try
            {
                base.PreExecute(character, "gold", args);
            }
            catch (Exception e)
            {
                SendColorMessage(character, Color.Red, e.Message);
                _log.Error(e.Message);
                _log.Error(e.StackTrace);
            }
        }
    }
}
