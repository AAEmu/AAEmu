using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

// SC_PACKET_GAME_RULE_CONFIG (700). Body:
//   indunCount u32, indunCount × { type u16, pvp bool, duel bool }
//   conflictCount u32, conflictCount × { type u16, peaceMin u32 }
// Sent empty during context establishment (no indun/conflict rules to advertise).
public class SCGameRuleConfigPacket() : GamePacket(SCOffsets.SCGameRuleConfigPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(0u); // indunCount
        stream.Write(0u); // conflictCount
        return stream;
    }
}
