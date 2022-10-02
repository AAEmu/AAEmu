using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Utils.Scripts.SubCommands.Feature;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.Commands
{
    public class FeatureCmd : SubCommandBase, ICommand, ICommandV2
    {
        public FeatureCmd()
        {
            Title = "[Feature]";
            Description = "Root command to manage Feature";
            CallPrefix = $"{CommandManager.CommandPrefix}feature";

            Register(new FeatureSetSubCommand(), "set", "s");
            Register(new FeatureCheckSubCommand(), "check", "c");
        }
        public void OnLoad()
        {
            string[] name = { "feature", "fset", "fs"};
            CommandManager.Instance.Register(name, this);
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
