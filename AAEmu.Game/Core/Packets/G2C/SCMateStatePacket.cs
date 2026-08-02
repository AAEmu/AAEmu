using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// chargeCount — each a u32 count followed by three i32 per entry. Same shape as SCSlaveState.
/// </summary>
public class SCMateStatePacket(uint objId) : GamePacket(SCOffsets.SCMateStatePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId);
        stream.Write(0); // skillCount
        stream.Write(0); // tagCount
        stream.Write(0); // chargeCount
        return stream;
    }
}
