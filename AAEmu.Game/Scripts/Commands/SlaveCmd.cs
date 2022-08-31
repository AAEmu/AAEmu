using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.Commands
{
    public class SlaveCmd : SubCommandBase, ICommand, ICommandV2
    {
        public SlaveCmd()
        {
            Title = "[Slave]";
            Description = "Root command to manage Slaves";
            CallPrefix = $"{CommandManager.CommandPrefix}slave";

            Register(new SlaveInformationSubCommand(), "info");
            Register(new SlavePositionSubCommand(), "position", "pos");
            Register(new SlaveSaveSubCommand(), "save");
            Register(new SlaveSpawnSubCommand(), "spawn");
            Register(new SlaveRemoveSubCommand(), "remove");
        }
        public void OnLoad()
        {
            CommandManager.Instance.Register("slave", this);
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
