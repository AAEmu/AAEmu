using System;
using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Auction;

namespace AAEmu.Game.Scripts.Commands
{
    class TestAuctionHouse : ICommand
    {
        public void OnLoad()
        {
            string[] name = { "testauctionhouse","testah" };
            CommandManager.Instance.Register(name, this);
        }

        public string GetCommandLineHelp()
        {
            return "";
        }

        public string GetCommandHelpText()
        {
            return "Adds every item into the auction house.";
        }

        public void Execute(Character character, string[] args)
        {
            var allItems = ItemManager.Instance.GetAllItems();
            character.SendMessage($"Trying to add {allItems.Count} items to the Auction House!");
            
            var amount = 0;
            foreach (var item in allItems)
            {
                var newAuctionItem = new AuctionItem
                {
                    ID = AuctionManager.Instance.GetNextId(),
                    Duration = 5,
                    ItemID = item.Id,
                    ObjectID = 0,
                    Grade = 0,
                    Flags = 0,
                    StackSize = 1,
                    DetailType = 0,
                    CreationTime = DateTime.Now,
                    EndTime = DateTime.Now.AddSeconds(172800),
                    LifespanMins = 0,
                    Type1 = 0,
                    WorldId = 0,
                    UnpackDateTIme = DateTime.Now,
                    UnsecureDateTime = DateTime.Now,
                    WorldId2 = 0,
                    ClientId = 0,
                    ClientName = "",
                    StartMoney = 0,
                    DirectMoney = 1,
                    GameServerID = 0,
                    BidderId = 0,
                    BidderName = "",
                    BidMoney = 0,
                    Extra = 0,
                    IsDirty = true
                };
                
                AuctionManager.Instance.AddAuctionItem(newAuctionItem);
                amount++;
            }

            character.SendMessage($"Added {amount} items to the Auction House!");
        }
    }
}
