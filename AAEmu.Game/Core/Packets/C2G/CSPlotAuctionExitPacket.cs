using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Withdraws the character's standing plot-auction bid (the Exit button).
/// </summary>
public class CSPlotAuctionExitPacket() : GamePacket(CSOffsets.CSPlotAuctionExitPacket, 1)
{
    public uint ActivityId { get; private set; }
    public uint AuctionConfigId { get; private set; }

    public override void Read(PacketStream stream)
    {
        ActivityId = stream.ReadUInt32();
        AuctionConfigId = stream.ReadUInt32();

        var character = Connection.ActiveChar;
        if (character == null)
            return;

        PlotAuctionManager.Instance.ExitBid(character, ActivityId, AuctionConfigId);
    }
}
