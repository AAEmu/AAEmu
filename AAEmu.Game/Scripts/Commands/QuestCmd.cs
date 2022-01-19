using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils;

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
            return "<list||add||remove||update||step||reward>";
        }

        public string GetCommandHelpText()
        {
            return "/quest <list||add||remove||update||step||reward>";
        }

        public void Execute( Character character, string[] args )
        {
            if ( args.Length < 1 )
            {
                character.SendMessage( "/quest <list||add||remove||update||step||reward>" );
                return;
            }

            QuestCommandUtil.GetCommandChoice( character, args[0], args );
        }
    }
}
