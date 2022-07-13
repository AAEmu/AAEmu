
namespace AAEmu.Game.Utils.Scripts.SubCommands
{
    public class DoodadPhaseSubCommand : SubCommandBase
    {
        public DoodadPhaseSubCommand()
        {
            Title = "[Doodad Phase]";
            Description = "Allow phase operations on a doodad";
            CallPrefix = "/doodad phase <list||change>";

            Register(new DoodadPhaseListSubCommand(), "list");
            Register(new DoodadPhaseChangeSubCommand(), "change");
        }
    }
}
