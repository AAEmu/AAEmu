using System.Collections.Generic;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Scripts.Commands
{
    public class NpcCmd : ICommand
    {
        public void OnLoad()
        {
            CommandManager.Instance.Register( "npc", this );
        }

        public string GetCommandLineHelp()
        {
            return "<save||pos||remove>";
        }

        public string GetCommandHelpText()
{
            return "[Npc] /npc save||remove <ObjId> || pos <ObjId> <x> <y> <z> <rx> <ry> <rz> - Use x y z roll pitch yaw instead of a value to keep current position";
        }

        public void Execute( Character character, string[] args )
        {
            if ( args.Length < 1 )
            {
                character.SendMessage( "[Npc] /npc save||remove <ObjId> || pos <ObjId> <x> <y> <z> <rx> <ry> <rz> - Use x y z roll pitch yaw instead of a value to keep current position" );
                return;
            }

            NpcCommandUtil.GetCommandChoice( character, args[0], args );
        }
    }
}
