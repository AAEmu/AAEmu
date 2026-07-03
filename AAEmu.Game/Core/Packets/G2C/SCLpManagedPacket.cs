using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCLpManagedPacket(uint characterId) : GamePacket(SCOffsets.SCLpManagedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        // 10.0.2.13: a single i64 "type" (charId).
        stream.Write((ulong)characterId);
        return stream;
    }
}
