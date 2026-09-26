using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// Native evidence: ArcheAge 10.0.2.13 serializer at, packet functor at,
/// receiver at, and loading-world state consumer at.
/// </remarks>
public class SCSnowingEverywherePacket(bool on) : GamePacket(SCOffsets.SCSnowingEverywherePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(on);
        return stream;
    }
}
