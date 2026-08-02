using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// 10.0.2.13 reads one u64 its serializer calls "chat" — the packed channel handle. The three fields 1.2
/// wrote occupy the same eight bytes in the same order, so the wire is unchanged; composing the value makes
/// what the client actually reads explicit rather than leaving it to field adjacency.
/// </remarks>
public class SCLeavedChatChannelPacket(ChatType type, short subType, FactionsEnum factionId)
    : GamePacket(SCOffsets.SCLeavedChatChannelPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        var chat = (ulong)(ushort)type
                   | ((ulong)(ushort)subType << 16)
                   | ((ulong)(uint)factionId << 32);
        stream.Write(chat);
        return stream;
    }
}
