using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCharacterInvenContentsPacket(SlotType type, byte numChunks, byte startChunkIdx, Item[] items)
    : GamePacket(SCOffsets.SCCharacterInvenContentsPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)type);
        stream.Write(numChunks);
        stream.Write(startChunkIdx);
        foreach (var item in items)
        {
            if (item == null)
                stream.Write(0);
            else
                stream.Write(item);
        }

        return stream;
    }
}
