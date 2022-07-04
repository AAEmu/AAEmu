
namespace AAEmu.Game.Utils.Scripts.SubCommands
{
    public class DoodadPhaseSubCommand : SubCommandBase
    {
        public DoodadPhaseSubCommand()
        {
            Prefix = "[Doodad Phase]";
            Description = "Allow phase operations on a doodad";
            CallExample = "/doodad phase <list||change> <ObjId>";

            Register(new DoodadPhaseListSubCommand(), "list");
            Register(new DoodadPhaseChangeSubCommand(), "change");
        }
    }
}
