using System;
using System.Collections.Generic;
using System.Text;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Tasks;

namespace AAEmu.Game.Models.Game.Auction
{
    public class AuctionHouseTask : Task
    {
        public override void Execute()
        {
            AuctionManager.Instance.UpdateAuctionHouse();
        }
    }
}
