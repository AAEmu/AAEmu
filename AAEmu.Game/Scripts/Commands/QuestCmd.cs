using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.Commands
{
    public class QuestCmd : ICommand
    {
        public void OnLoad()
        {
            CommandManager.Instance.Register( "quest", this );
        }

        public string GetCommandLineHelp()
        {
            return "<list||add||remove||prog||reward>";
        }

        public string GetCommandHelpText()
{
            return "[Quest] /quest <add/remove/list/prog/reward>\nBefore that, target the Npc you need for the quest";
        }

        public void Execute( Character character, string[] args )
        {
            if ( args.Length < 1 )
            {
                character.SendMessage( "[Quest] /quest <add/remove/list/prog/reward>\nBefore that, target the Npc you need for the quest" );
                return;
            }

            QuestCommandUtil.GetCommandChoice( character, args[0], args );
        }
    }
}
