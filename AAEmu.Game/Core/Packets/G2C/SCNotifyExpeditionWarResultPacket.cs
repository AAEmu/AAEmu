using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Guild War end result. Without it the client shows every war as a draw. Wire: u32 id, u32 id2,
/// u8 result (1 = id won, 2 = id2 won, 0 = draw).
/// </summary>
public class SCNotifyExpeditionWarResultPacket(uint id, uint id2, byte result) : GamePacket(SCOffsets.SCNotifyExpeditionWarResultPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(id);
        stream.Write(id2);
        stream.Write(result);
        return stream;
    }
}
