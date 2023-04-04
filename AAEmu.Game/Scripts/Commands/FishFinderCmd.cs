using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Utils.Scripts.SubCommands.FishFinder;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.Commands
{
    public class FishFinderCmd : SubCommandBase, ICommand, ICommandV2
    {
        public FishFinderCmd()
        {
            Title = "[FishFinder]";
            Description = "Root command to manage FishFinder";
            CallPrefix = $"{CommandManager.CommandPrefix}fishfinder";

            Register(new FishFinderSetSubCommand(), "set", "s");
        }
        public void OnLoad()
        {
            string[] name = { "fishfinder", "ff" };
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
