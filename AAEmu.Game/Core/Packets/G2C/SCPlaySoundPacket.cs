using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCPlaySoundPacket(byte kind, uint bubbleId, uint npcObjId) : GamePacket(SCOffsets.SCPlaySoundPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(kind);
        stream.Write(bubbleId);
        stream.Write(npcObjId);

        return stream;
    }
}
