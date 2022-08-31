using AAEmu.Game.Core.Managers;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.Commands
{
    public class DoodadPhaseSubCommand : SubCommandBase
    {
        public DoodadPhaseSubCommand()
        {
            Title = "[Doodad Phase]";
            Description = "Allow phase operations on a doodad";
            CallPrefix = $"{CommandManager.CommandPrefix}doodad phase";

            Register(new DoodadPhaseListSubCommand(), "list");
            Register(new DoodadPhaseChangeSubCommand(), "change");
        }
    }
}
