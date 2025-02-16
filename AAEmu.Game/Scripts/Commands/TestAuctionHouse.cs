//using System;
using AAEmu.Game.Core.Managers;
//using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Auction;
//using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Utils.Scripts;
//using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Scripts.Commands;

internal class TestAuctionHouse : ICommand
{
    public string[] CommandNames { get; set; } = new string[] { "testauctionhouse", "testah" };

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
        return "Adds every item into the auction house.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var allItems = ItemManager.Instance.GetAllItems();
        CommandManager.SendNormalText(this, messageOutput, $"Trying to add {allItems.Count} items to the Auction House!");

        var amount = 0;
        foreach (var itemTemplate in allItems)
        {
            if (itemTemplate == null)
            {
                continue;
            }

            var item = ItemManager.Instance.Create(itemTemplate.Id, 1, 0);
            if (item == null)
            {
                continue;
            }
            // Create a new auction item
            var newAuctionItem = AuctionManager.Instance.CreateAuctionLot(character, item, 0, 1, AuctionDuration.AuctionDuration6Hours, 1, 1);

            // Add the auction item to the auction house
            AuctionManager.Instance.AddAuctionLot(newAuctionItem);
            amount++;
        }

        CommandManager.SendNormalText(this, messageOutput, $"Added {amount} items to the Auction House!");
    }
}
