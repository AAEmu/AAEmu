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
            CommandManager.Instance.Register( "doodad", this );
        }

        public string GetCommandLineHelp()
        {
            return "<chain||setphase||save||pos>";
        }

        public string GetCommandHelpText()
{
            return "[Doodad] /doodad [chain <TemplateId>]||[setphase||save <ObjId>]||[pos <ObjId> <x> <y> <z> <rx> <ry> <rz>] - Use x y z roll pitch yaw instead of a value to keep current position";
        }

        public void Execute( Character character, string[] args )
        {
            if ( args.Length < 1 )
            {
                character.SendMessage( "[Doodad] /doodad [chain <TemplateId>]||[setphase||save <ObjId>]||[pos <ObjId> <x> <y> <z> <rx> <ry> <rz>] - Use x y z roll pitch yaw instead of a value to keep current position" );
                return;
            }

            DoodadCommandUtil.GetCommandChoice( character, args[0], args );
        }
    }
}
