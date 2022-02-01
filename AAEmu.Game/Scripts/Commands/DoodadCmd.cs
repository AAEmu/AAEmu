using System.Collections.Generic;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Scripts.Commands
{
    public class DoodadCmd : ICommand
    {
        public void OnLoad()
        {
            CommandManager.Instance.Register( "doodad_chain", this );
        }

        public string GetCommandLineHelp()
        {
            return "<list>";
        }

        public string GetCommandHelpText()
{
            return "[Doodad] /doodad_chain list <TemplateId>";
        }

        public void Execute( Character character, string[] args )
        {
            if ( args.Length < 1 )
            {
                character.SendMessage( "[Doodad] /doodad_chain list <TemplateId>" );
                return;
            }

            DoodadCommandUtil.GetCommandChoice( character, args[0], args );
        }
    }
}
