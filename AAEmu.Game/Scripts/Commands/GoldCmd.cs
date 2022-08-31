using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.Commands
{
    public class GoldCmd : SubCommandBase, ICommand, ICommandV2
    {
        public GoldCmd()
        {
            Title = "[Gold]";
            Description = "Root command to manage gold";
            CallPrefix = $"{CommandManager.CommandPrefix}gold/";

            Register(new GoldSetSubCommand(), "add");
            Register(new GoldSetSubCommand(), "set");
            Register(new GoldSetSubCommand(), "remove");
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
            return CallPrefix;
        }
        public void Execute(Character character, string[] args)
        {
            throw new InvalidOperationException($"A {nameof(ICommandV2)} implementation should not be used as ICommand interface");
        }
    }
}
