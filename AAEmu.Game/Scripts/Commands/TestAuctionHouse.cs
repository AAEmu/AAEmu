using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Auction;
using AAEmu.Game.Utils.Scripts;

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
        CommandManager.SendNormalText(this, messageOutput,
            $"Trying to add {allItems.Count} items to the Auction House!");

        var amount = 0;
        foreach (var item in allItems)
        {
            var newAuctionItem = new AuctionItem
            {
                Id = AuctionManager.Instance.GetNextId(),
                Duration = 5,
                ItemId = item.Id,
                ObjectId = 0,
                Grade = 0,
                Flags = 0,
                StackSize = 1,
                DetailType = 0,
                CreationTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow.AddSeconds(172800),
                LifespanMins = 0,
                Type1 = 0,
                WorldId = 0,
                UnpackDateTime = DateTime.UtcNow,
                UnsecureDateTime = DateTime.UtcNow,
                WorldId2 = 0,
                ClientId = 0,
                ClientName = "",
                StartMoney = 0,
                DirectMoney = 1,
                BidWorldId = 0,
                BidderId = 0,
                BidderName = "",
                BidMoney = 0,
                Extra = 0,
                IsDirty = true
            };

            AuctionManager.Instance.AddAuctionItem(newAuctionItem);
            amount++;
        }

        CommandManager.SendNormalText(this, messageOutput, $"Added {amount} items to the Auction House!");
    }
}
