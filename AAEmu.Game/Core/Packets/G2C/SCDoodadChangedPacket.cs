using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCDoodadChangedPacket(uint id, int data) : GamePacket(SCOffsets.SCDoodadChangedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(id);
        stream.Write(data);
        return stream;
    }
}
