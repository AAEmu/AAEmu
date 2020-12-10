using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Scripts.Commands
{
    public class Dist: ICommand
    {
        public void OnLoad()
        {
            string[] name = { "dist", "distance" };
            CommandManager.Instance.Register(name, this);
        }

        public string GetCommandLineHelp()
        {
            return "";
        }

        public string GetCommandHelpText()
        {
            return "Gets distance using various calculations with target";
        }

        public void Execute(Character character, string[] args)
        {
            var target = character.CurrentTarget;

            var rawDistance = MathUtil.CalculateDistance(character.Position, target.Position);
            var rawDistanceZ = MathUtil.CalculateDistance(character.Position, target.Position, true);
            var modelDistance = character.GetDistanceTo(target);
            var modelDistanceZ = character.GetDistanceTo(target, true);

            character.SendMessage("[Distance]\nRaw distance : {0}\nRaw distance (Z) : {1}\nModel adjusted distance: {2}\nModel adjusted distance (Z): {3}", rawDistance, rawDistanceZ, modelDistance, modelDistanceZ);
        }
    }
}
