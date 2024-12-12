using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

internal class ReloadAuction : ICommand
{
    public string[] CommandNames { get; set; } = ["reloadauction", "reload_auction", "reloadah", "reload_ah"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "";
    }

    public string GetCommandHelpText()
    {
        return "Reloads the AuctionManager";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        AuctionManager.Instance.Load();
    }
}
