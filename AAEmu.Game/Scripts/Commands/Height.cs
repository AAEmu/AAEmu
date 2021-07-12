using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Scripts.Commands
{
    public class Height : ICommand
    {
        public void OnLoad()
        {
            CommandManager.Instance.Register("height", this);
        }

        public string GetCommandLineHelp()
        {
            return "(target)";
        }

        public string GetCommandHelpText()
        {
            return "Gets your or target's current height and that of the supposed floor (using heightmap data)";
        }

        public void Execute(Character character, string[] args)
        {
            Character targetPlayer = character;
            if (args.Length > 0)
                targetPlayer = WorldManager.Instance.GetTargetOrSelf(character, args[0], out var firstarg);

            var height = WorldManager.Instance.GetHeight(targetPlayer.Transform.ZoneId, targetPlayer.Transform.World.Position.X, targetPlayer.Transform.World.Position.Y);
            character.SendMessage("[Height] {2} Z-Pos: {0} - Floor: {1}", character.Transform.World.Position.Z, height, targetPlayer.Name);
        }
    }
}
