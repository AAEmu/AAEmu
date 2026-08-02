using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// 10.0.2.13 body: two i32s the serializer calls "type" — the quest context id and the component. The
/// 8-byte block 1.2 sent between them is not read, and shifted the component by eight bytes.
/// </remarks>
public class SCQuestContextCompletedPacket(uint questId, uint componentId)
    : GamePacket(SCOffsets.SCQuestContextCompletedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((int)questId);
        stream.Write((int)componentId);
        return stream;
    }
}
