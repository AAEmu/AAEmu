using System;
using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.Commands
{
    public class ItemCmd : SubCommandBase, ICommand, ICommandV2
    {
        public ItemCmd()
        {
            Title = "[Item]";
            Description = "Root command to manage Items";
            CallPrefix = $"{CommandManager.CommandPrefix}item";

            Register(new ItemAddSubCommand(), "add");
            Register(new ItemExpireSubCommand(), "expire");
            Register(new ItemUnwrapSubCommand(), "unwrap");
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
            throw new InvalidOperationException($"A {nameof(ICommandV2)} implementation should not be used as ICommand interface");
        }
    }
}
