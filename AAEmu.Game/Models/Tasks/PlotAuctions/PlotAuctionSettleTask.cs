using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks.PlotAuctions;

/// <summary>Settles plot auctions whose bid window closed — including while the process was
/// down, the moment Initialize runs.</summary>
public class PlotAuctionSettleTask : Task
{
    public override void Execute()
    {
        PlotAuctionManager.Instance.SweepSettlement();
    }
}
