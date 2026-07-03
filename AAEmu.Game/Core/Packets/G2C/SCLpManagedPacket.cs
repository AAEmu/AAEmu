using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCLpManagedPacket(uint characterId) : GamePacket(SCOffsets.SCLpManagedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        // 10.0.2.13 (x2game-dev_dedicate SCLpManaged serializer sub_39C31B30): a single i64 "type" (charId).
        stream.Write((ulong)characterId);
        return stream;
    }
}
