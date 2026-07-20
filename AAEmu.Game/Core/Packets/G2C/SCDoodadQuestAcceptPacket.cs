using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCDoodadQuestAcceptPacket(uint doodadObjId, uint questContextId)
    : GamePacket(SCOffsets.SCDoodadQuestAcceptPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(doodadObjId);
        stream.Write(questContextId);

        return stream;
    }
}
