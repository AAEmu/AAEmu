using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>Publishes purchase counters for limited Marketplace goods.</summary>
public class SCICSBuyCountPacket(uint kind, IReadOnlyList<(uint ShopId, uint BuyCount)> entries)
    : GamePacket(SCOffsets.SCICSBuyCountPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        var n = (short)Math.Min(entries.Count, 200);
        stream.Write(kind);
        stream.Write(n);
        for (var i = 0; i < n; i++)
            stream.Write(entries[i].ShopId);
        for (var i = 0; i < n; i++)
            stream.Write(entries[i].BuyCount);
        return stream;
    }
}
