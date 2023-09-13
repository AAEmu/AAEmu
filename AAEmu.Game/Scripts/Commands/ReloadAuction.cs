using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Scripts.Commands
{
    class ReloadAuction : ICommand
    {
        public void OnLoad()
        {
            string[] name = { "reloadauction", "reload_auction", "reloadah", "reload_ah" };
            CommandManager.Instance.Register(name, this);
        }

        public string GetCommandLineHelp()
        {
            return "";
        }

        public string GetCommandHelpText()
        {
            return "Reloads the AuctionManager";
        }
        public void Execute(Character character, string[] args)
        {
            AuctionManager.Instance.Load();
        }
    }
}
