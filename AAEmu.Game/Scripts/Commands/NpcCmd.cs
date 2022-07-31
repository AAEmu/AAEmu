using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.Commands
{
    public class NpcCmd : SubCommandBase, ICommand, ICommandV2
    {
        public NpcCmd()
        {
            Title = "[Npc]";
            Description = "Root command to manage Npcs";
            CallPrefix = $"{CommandManager.CommandPrefix}npc";

            Register(new NpcInformationSubCommand(), "info");
            Register(new NpcPositionSubCommand(), "position", "pos");
            Register(new NpcSaveSubCommand(), "save");
            Register(new NpcSpawnSubCommand(), "spawn");
            Register(new NpcRemoveSubCommand(), "remove");
        }
        public void OnLoad()
        {
            CommandManager.Instance.Register("npc", this);
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
