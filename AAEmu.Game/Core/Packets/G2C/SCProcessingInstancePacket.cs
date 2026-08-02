using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// 10.0.2.13 body, named by its own serializer: u32 zoneId, u32 state. 1.2 stopped at the zone, so the
/// client read the following packet's first four bytes as the instance state.
/// </remarks>
public class SCProcessingInstancePacket(int zoneId, uint state = 0) : GamePacket(SCOffsets.SCProcessingInstancePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(zoneId);
        stream.Write(state);
        return stream;
    }
}
