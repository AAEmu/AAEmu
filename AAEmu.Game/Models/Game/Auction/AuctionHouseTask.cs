using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Tasks;

namespace AAEmu.Game.Models.Game.Auction;

public class AuctionHouseTask : Task
{
    public override System.Threading.Tasks.Task ExecuteAsync()
    {
        AuctionManager.Instance.UpdateAuctionHouse();
        return System.Threading.Tasks.Task.CompletedTask;
    }
}
