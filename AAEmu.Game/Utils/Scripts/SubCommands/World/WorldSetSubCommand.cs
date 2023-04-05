using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Utils.Scripts.SubCommands.World
{
    public class WorldSetSubCommand : SubCommandBase
    {
        public WorldSetSubCommand()
        {
            Title = "[World Set]";
            Description = "Possible operations with world configuration";
            CallPrefix = $"{CommandManager.CommandPrefix}set";

            Register(new WorldSetGrowthrateSubCommand(), "growthrate", "growth_rate", "gr");
            Register(new WorldSetLootrateSubCommand(), "lootrate", "loot_rate", "lr");
            Register(new WorldSetVocationrateSubCommand(), "vocationrate", "vocation_rate", "vr");
            Register(new WorldSetHonorrateSubCommand(), "honorrate", "honor_rate", "hr");
            Register(new WorldSetExprateSubCommand(), "exprate", "exp_rate", "er");
            Register(new WorldSetAutosaveintervalSubCommand(), "autosaveinterval", "auto_save_interval", "asi");
            Register(new WorldSetLogoutmessageSubCommand(), "logoutmessage","logout_message", "lm");
            Register(new WorldSetMotdmessageSubCommand(), "motd");
        }
    }
}
