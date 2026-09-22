using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Asks for the plot-auction bid map of one activity. The client's call sites put the config
/// id in the plotId slot (limited_auction_tab.lua line 273), so this is config-id shaped on
/// both directions of the wire.
/// </summary>
public class CSPlotAuctionQueryInfoPacket() : GamePacket(CSOffsets.CSPlotAuctionQueryInfoPacket, 1)
{
    public uint ActivityId { get; private set; }
    public uint PlotId { get; private set; }

    public override void Read(PacketStream stream)
    {
        ActivityId = stream.ReadUInt32();
        PlotId = stream.ReadUInt32();

        var character = Connection.ActiveChar;
        if (character == null)
            return;

        PlotAuctionManager.Instance.QueryInfo(character, ActivityId, PlotId);
    }
}
