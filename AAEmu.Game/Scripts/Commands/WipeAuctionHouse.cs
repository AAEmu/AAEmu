using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

internal class WipeAuctionHouse : ICommand
{
    public string[] CommandNames { get; set; } = new string[] { "wipeauctionhouse", "wipeah" };

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "<WIPE>";
    }

    public string GetCommandHelpText()
    {
        return
            "Deletes ALL Items from the AH. No going back from this. Use WIPE as a argument to actual wipe the items";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length < 1 || args[0] != "WIPE")
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        AuctionManager.Instance.Load();

        foreach (var item in AuctionManager.Instance.AuctionLots)
        {
            AuctionManager.Instance._deletedAuctionItemIds.Add((long)item.Id);
        }

        AuctionManager.Instance.AuctionLots.Clear();
        SaveManager.Instance.DoSave();
        AuctionManager.Instance.Load();
    }
}
