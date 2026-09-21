using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// Native evidence: ArcheAge 10.0.2.13 serializer at 0x39C51EB0, packet functor at 0x3954F6F0,
/// receiver at 0x394DABD0, and loading-world state consumer at 0x394AFF00.
/// </remarks>
public class SCSnowingEverywherePacket(bool on) : GamePacket(SCOffsets.SCSnowingEverywherePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(on);
        return stream;
    }
}
