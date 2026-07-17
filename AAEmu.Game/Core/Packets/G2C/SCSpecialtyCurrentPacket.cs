using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCSpecialtyCurrentPacket(ushort fromZoneGroup, ushort toZoneGroup, List<(uint, uint)> results)
    : GamePacket(SCOffsets.SCSpecialtyCurrentPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(results.Count);
        stream.Write(fromZoneGroup);
        stream.Write(toZoneGroup);
        foreach (var (itemId, rate) in results)
        {
            stream.Write(itemId);
            stream.Write(rate);
        }
        return stream;
    }
}
