using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Core.Managers.World;

namespace AAEmu.Game.Scripts.Commands
{
    class WipeAuctionHouse : ICommand
    {
        public void OnLoad()
        {
            string[] name = { "wipeauctionhouse", "wipeah"};
            CommandManager.Instance.Register(name, this);
        }

        public string GetCommandLineHelp()
        {
            return "Deletes ALL Items from the AH. No going back from this.";
        }

        public string GetCommandHelpText()
        {
            return "Deletes ALL Items from the AH. No going back from this.";
        }
        public void Execute(Character character, string[] args)
        {
            AuctionManager.Instance.Load();

            foreach (var item in AuctionManager.Instance._auctionItems)
            {
                AuctionManager.Instance._deletedAuctionItemIds.Add((long)item.ID);
            }

            AuctionManager.Instance._auctionItems.Clear();
            SaveManager.Instance.DoSave();
            AuctionManager.Instance.Load();
        }
    }
}
