using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Managers.World;

namespace AAEmu.Game.Scripts.Commands
{
    public class AddXP : ICommand
    {
        public void OnLoad()
        {
            string[] name = { "addxp", "add_xp", "givexp", "xp" };
            CommandManager.Instance.Register(name, this);
        }

        public string GetCommandLineHelp()
        {
            return "(target) <xp>";
        }

        public string GetCommandHelpText()
        {
            return "Adds experience points (to target player)";
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length == 0)
            {
                character.SendMessage("[XP] " + CommandManager.CommandPrefix + "add_xp (target) <exp>");
                return;
            }

            Character targetPlayer = WorldManager.Instance.GetTargetOrSelf(character, args[0], out var firstarg);

            var xptoadd = 0;
            if (int.TryParse(args[firstarg + 0], out int parsexp))
            {
                xptoadd = parsexp;
            }

            if (xptoadd > 0)
                targetPlayer.AddExp(xptoadd, true);
        }
    }
}
