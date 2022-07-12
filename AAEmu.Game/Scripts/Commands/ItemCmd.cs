using System;
using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.Commands
{
    public class ItemCmd : SubCommandBase, ICommand, ISubCommand
    {
        public ItemCmd()
        {
            Title = "[Item]";
            Description = "Root command to manage Items";
            CallPrefix = "/item [add]";

            Register(new ItemAddSubCommand(), "add");
        }
        public void OnLoad()
        {
            CommandManager.Instance.Register("item", this);
        }

        public string GetCommandLineHelp()
        {
            return $"<{string.Join("||", SupportedCommands)}>";
        }

        public string GetCommandHelpText()
        {
            return CallPrefix;
        }

        public void Execute(Character character, string[] args)
        {
            try
            {
                base.PreExecute(character, "item", args);
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
