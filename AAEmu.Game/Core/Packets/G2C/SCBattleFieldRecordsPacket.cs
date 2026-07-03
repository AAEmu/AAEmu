using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCBattleFieldRecordsPacket() : GamePacket(SCOffsets.SCBattleFieldRecordsPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        // Count of battlefield record entries; the reference sends 0 at world entry.
        stream.Write(0u);

        return stream;
    }
}
