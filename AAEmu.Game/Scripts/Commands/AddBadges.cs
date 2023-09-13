using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Managers.World;

namespace AAEmu.Game.Scripts.Commands
{
    public class AddBadges : ICommand
    {
        public void OnLoad()
        {
            string[] name = { "VocationPoints", "add_vp", "add_vb", "vp" };
            CommandManager.Instance.Register(name, this);
        }

        public string GetCommandLineHelp()
        {
            return "(target) <VocationPoints>";
        }

        public string GetCommandHelpText()
        {
            return "Adds VocationPoints (to target player)";
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length == 0)
            {
                character.SendMessage("[VocationPoint] " + CommandManager.CommandPrefix + "add_vp (target) <VocationPoint>");
                return;
            }

            var targetPlayer = WorldManager.GetTargetOrSelf(character, args[0], out var firstArg);

            if (!int.TryParse(args[firstArg], out var vpToAdd))
                vpToAdd = 0;

            if (vpToAdd != 0)
                targetPlayer.ChangeGamePoints((GamePointKind)1, vpToAdd);
        }
    }
}
