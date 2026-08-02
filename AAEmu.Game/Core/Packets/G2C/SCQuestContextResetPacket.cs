using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// 10.0.2.13 reads a single i32 the serializer names "type" — the quest context id. The 1.2 body that
/// followed it (an 8-byte block and the component id) is not read, and sending it left the client parsing
/// the next packet from the wrong offset.
/// </remarks>
public class SCQuestContextResetPacket(uint questId)
    : GamePacket(SCOffsets.SCQuestContextResetPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((int)questId);
        return stream;
    }
}
