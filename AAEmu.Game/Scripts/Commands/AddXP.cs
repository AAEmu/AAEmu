using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Scripts.Commands
{
    public class AddXP : ICommand
    {
        public void OnLoad()
        {
            CommandManager.Instance.Register("addxp", this);
        }

        public string GetCommandLineHelp()
        {
            return "<xp>";
        }

        public string GetCommandHelpText()
        {
            return "Adds experience points.";
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length == 0)
            {
                character.SendMessage("[AddXP] /addxp <exp>");
                return;
            }

            var xptoadd = 0;
            if (int.TryParse(args[0], out int parsexp))
            {
                xptoadd = parsexp;
            }

            if (xptoadd > 0)
                character.AddExp(xptoadd, true);
        }
    }
}
