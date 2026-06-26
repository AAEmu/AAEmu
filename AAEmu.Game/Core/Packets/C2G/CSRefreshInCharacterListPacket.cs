using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSRefreshInCharacterListPacket() : GamePacket(CSOffsets.CSRefreshInCharacterListPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        // Client polls this ~every 15s at char-select. There is NO SCRefreshInCharacterList packet in
        // 10.0.2.13 — AAEmu's SCRefreshInCharacterListPacket opcode (0x1CD) actually maps to the client's
        // SCUnderWaterPacket, so replying fed the client a garbage "under water" packet every 15s. The poll
        // needs no reply; just log it.
        Logger.Debug("RefreshInCharacterList (poll, no reply)");
    }
}
