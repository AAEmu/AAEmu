using System;
using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.Commands
{
    public class NpcCmd : SubCommandBase, ICommand, ISubCommand
    {
        public NpcCmd()
        {
            Title = "[Npc]";
            Description = "Root command to manage Npcs";
            CallPrefix = "/npc [info||save||remove||position]";

            Register(new NpcInformationSubCommand(), "info");
            Register(new NpcPositionSubCommand(), "position");
            Register(new NpcSaveSubCommand(), "save");
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
            try
            {
                base.PreExecute(character, "npc", args);
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
