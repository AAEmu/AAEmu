using System;
using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.Commands
{
    public class DoodadCmd : SubCommandBase, ICommand
    {
        public DoodadCmd() 
        {
            Title = "[Doodad]";
            Description = "Root command to manage Doodads";
            CallPrefix = $"{CommandManager.CommandPrefix}doodad";

            Register(new DoodadChainSubCommand(), "chain");
            Register(new DoodadPhaseSubCommand(), "phase", "setphase");
            Register(new DoodadSaveSubCommand(), "save");
            Register(new DoodadPositionSubCommand(), "position", "pos");
            Register(new DoodadSpawnSubCommand(), "spawn");
        }

        public void OnLoad()
        {
            CommandManager.Instance.Register("doodad", this);
        }

        public DoodadCmd(Dictionary<ICommandV2, string[]> subcommands) : base(subcommands)
        {

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
