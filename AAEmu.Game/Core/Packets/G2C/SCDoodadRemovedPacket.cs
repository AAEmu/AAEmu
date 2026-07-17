using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCDoodadRemovedPacket(uint id) : GamePacket(SCOffsets.SCDoodadRemovedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(id);
        stream.Write(false); // e if false then the doodad will be deleted
        return stream;
    }
}
