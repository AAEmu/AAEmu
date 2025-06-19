using AAEmu.Game.Core.Managers;
using Task = AAEmu.Game.Models.Tasks.Task;

namespace AAEmu.Game.Models.Game.Auction;

public class AuctionHouseTask : Task
{
    public override void Execute()
    {
        AuctionManager.Instance.UpdateAuctionHouse();
    }
}
